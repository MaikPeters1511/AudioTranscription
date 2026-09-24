# Story S17: Authentifizierung (lokale ASP.NET Core Identity)

> Epic: [E-01](./00-epic.openspec.md) · GitHub: #94 (Tasks #95–#102) · Phase 2 (vor S09) · Agenten: sec-agent, backend-agent, frontend-agent, architect-agent

## 1. Description
Alle API-Endpoints und der SignalR-Hub sind anonym. Mit den Aktionen aus S09 (Löschen, Abbrechen, Neu starten) könnte jeder, der die API erreicht, beliebige Jobs verändern. CLAUDE.md verlangt außerdem, dass Routen standardmäßig mit `[Authorize]` geschützt sind.

**Entscheidungen des Repo-Owners (D4, 2026-09-24):**
- **Lokale ASP.NET Core Identity** (Konten in der eigenen Datenbank, funktioniert offline), kein Entra ID.
- **Gemeinsame Jobs:** Alle angemeldeten Benutzer sehen und bearbeiten alle Jobs. Es gibt keine Zuordnung von Jobs zu Benutzern.

## 2. User Stories
- Als Betreiber möchte ich, dass nur angemeldete Benutzer Transkripte sehen, hochladen oder löschen können, damit private Aufnahmen geschützt sind.
- Als Benutzer möchte ich mich mit E-Mail und Passwort anmelden und abmelden können.
- Als Betreiber möchte ich den ersten Benutzer per Konfiguration anlegen, ohne dass sich Fremde selbst registrieren können.

## 3. UI/UX Requirements
- **Komponenten:** Login-Seite (`/login`, E-Mail, Passwort, Fehlermeldung), Anzeige des Benutzers und Logout in der Navbar.
- **Routing:** Ein Route-Guard leitet nicht angemeldete Benutzer auf `/login` um und nach dem Login zurück zur ursprünglichen Seite.
- **Sprache und Barrierefreiheit:** Alle Texte laufen über i18n (DE/EN). Formularfelder haben Labels, Fehler werden per `role="alert"` angesagt, das Formular ist vollständig per Tastatur bedienbar.

## 4. API & Backend Requirements
- **Identity:** `IdentityUser` über `AppDbContext : IdentityDbContext<IdentityUser>` mit neuer Migration. Die Baseline aus S04 bleibt gültig.
- **Endpoints:** `MapIdentityApi<IdentityUser>()` unter `/api/auth` mit Cookie-Login (`useCookies=true`), dazu `GET /api/auth/me` (angemeldeter Benutzer, sonst `401`).
  - `POST /api/auth/register` ist nur aktiv, wenn `Auth:AllowRegistration=true` gesetzt ist (Standard `false`). Sonst antwortet der Endpoint mit `404`.
- **Cookie:** `HttpOnly`, `Secure`, `SameSite=Strict`. Die API antwortet mit `401` bzw. `403` statt mit einem Redirect.
- **Autorisierung:** Eine Fallback-Policy verlangt einen angemeldeten Benutzer. Das gilt für alle `/api/audio-jobs`-Endpoints und den Hub `/hubs/transcription`. Ausnahmen (`AllowAnonymous`) sind nur Login und `me`. Die Health-Endpoints (`MapDefaultEndpoints`, nur in Development) bleiben frei.
- **Erster Benutzer:** Enthält die Datenbank noch keinen Benutzer, wird beim Start `Auth:InitialUser:Email` bzw. `Auth:InitialUser:Password` aus User-Secrets oder Umgebungsvariablen angelegt. Es gibt kein Secret im Code oder in `appsettings.json`.
- **CORS:** Nur noch Origins aus `Cors:AllowedOrigins` sind erlaubt. Heute gilt `SetIsOriginAllowed(_ => true)` zusammen mit `AllowCredentials`, was mit Cookie-Auth einem Cross-Site-Missbrauch die Tür öffnet.

## 5. Tasks

### S17-T1 ADR 0003: Authentifizierungskonzept · architect + sec · XS
- **AC:**
  - [ ] ADR mit den Entscheidungen (lokale Identity, Cookie, gemeinsame Jobs, keine offene Registrierung, CORS) und den verworfenen Alternativen liegt in `docs/adr/`.

### S17-T2 Identity-Persistenz und Migration · backend · S
- **AC (TDD):**
  - [ ] `AppDbContext` erbt von `IdentityDbContext<IdentityUser>`, dazu die Migration `AddIdentity`.
  - [ ] SQL-Server-Test: Neue DB und Legacy-DB (Baseline) erhalten die Identity-Tabellen, bestehende Jobs bleiben erhalten.

### S17-T3 Auth-Endpoints und Cookie-Konfiguration · backend + sec · M
- **AC (TDD):**
  - [ ] Integrationstests: Login mit korrekten Daten liefert `200` und setzt ein Cookie mit `HttpOnly`, `Secure` und `SameSite=Strict`. Falsche Daten liefern `401`.
  - [ ] Ohne Cookie liefert `GET /api/auth/me` `401`, mit Cookie die E-Mail.
  - [ ] `POST /api/auth/register` liefert `404`, solange `Auth:AllowRegistration` nicht gesetzt ist.
  - [ ] Logout löscht die Session.

### S17-T4 Endpoints und Hub schützen · backend + sec · S
- **AC (TDD):**
  - [ ] Ohne Anmeldung liefern alle `/api/audio-jobs`-Endpoints und die Hub-Negotiation `401`, mit Anmeldung funktionieren sie wie bisher.
  - [ ] Die bestehenden Integrationstests laufen mit einem Test-Auth-Handler.

### S17-T5 Ersten Benutzer aus der Konfiguration anlegen · backend · S
- **AC (TDD):**
  - [ ] Test: Leere DB mit gesetzter Konfiguration legt den Benutzer an. Existiert bereits ein Benutzer, passiert nichts. Fehlt die Konfiguration, wird eine Warnung geloggt und kein Benutzer angelegt.
  - [ ] Aspire-AppHost und docker-compose reichen die Werte als Parameter bzw. Umgebungsvariable durch, als Secret markiert.

### S17-T6 CORS einschränken · backend + sec · XS
- **AC (TDD):**
  - [ ] Test: Eine Origin aus `Cors:AllowedOrigins` erhält CORS-Header, eine fremde Origin nicht.

### S17-T7 Frontend: Login, Guard, Logout · frontend · M
- **AC (TDD):**
  - [ ] `AuthService` (Login, Logout, `me`) und Route-Guard mit Rücksprung zur ursprünglichen Seite.
  - [ ] Ein HTTP-Interceptor leitet bei `401` auf `/login` um.
  - [ ] Der SignalR-Aufbau erfolgt erst nach dem Login.
  - [ ] Die Login-Seite ist übersetzt (DE/EN) und barrierefrei.
  - [ ] Unit-Tests für Service, Guard, Interceptor und Login-Komponente.

### S17-T8 Dokumentation · doc · XS
- **AC:**
  - [ ] README beschreibt die Einrichtung (User-Secrets bzw. Umgebungsvariablen für den ersten Benutzer, Registrierung, CORS).
  - [ ] Swagger bzw. OpenAPI ist aktuell.

## 6. Acceptance Criteria (DoD)
- [ ] Ohne Login ist über die API kein Job sichtbar und keine Aktion möglich, mit Login funktioniert die App wie bisher.
- [ ] Kein Secret im Repository. Der Security-Check von `sec-agent` ist durchgeführt.
