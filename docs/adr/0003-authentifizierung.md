# ADR 0003: Authentifizierung mit lokaler ASP.NET Core Identity

- **Status:** Akzeptiert
- **Datum:** 2026-09-24
- **Entscheider:** Repo-Owner (D4), umgesetzt in Story S17 (#94)

## Kontext

API und SignalR-Hub waren anonym erreichbar. Mit den geplanten Aktionen aus S09 (Löschen, Abbrechen, Neu starten) hätte jeder, der die API erreicht, beliebige Jobs verändern oder löschen können. CLAUDE.md verlangt, dass Backend-Routen standardmäßig geschützt sind. Die App wirbt mit „privat & offline“.

## Entscheidung

1. **Lokale ASP.NET Core Identity** (`IdentityUser` in der eigenen Datenbank, EF-Core-Stores). Kein externer Identity Provider.
2. **Cookie-Authentifizierung über die Identity API Endpoints** (`MapIdentityApi` unter `/api/auth`, Login mit `useCookies=true`). Das Cookie ist `HttpOnly`, `Secure` und `SameSite=Strict`. Bei fehlender Anmeldung antwortet die API mit `401` statt mit einem Redirect.
3. **Standardmäßig geschützt:** Eine Fallback-Policy verlangt einen angemeldeten Benutzer. Ausnahmen sind nur Login, `me` und die Health-Endpoints (nur in Development).
4. **Gemeinsame Jobs:** Alle angemeldeten Benutzer sehen und bearbeiten alle Jobs. Es gibt keine Zuordnung von Jobs zu Benutzern.
5. **Keine offene Registrierung:** `POST /api/auth/register` ist nur mit `Auth:AllowRegistration=true` aktiv. Der erste Benutzer wird beim Start aus `Auth:InitialUser:Email`/`Password` angelegt, die aus User-Secrets oder Umgebungsvariablen kommen, nie aus dem Repository.
6. **CORS** erlaubt nur noch Origins aus `Cors:AllowedOrigins`.

## Verworfene Alternativen

- **Microsoft Entra ID (`Microsoft.Identity.Web`):** Braucht Internet und eine App-Registrierung in Azure. Das widerspricht dem Offline-Anspruch.
- **Bearer-Token statt Cookie:** Das Token müsste im Browser gespeichert werden (XSS-Risiko) und für SignalR zusätzlich per Query-String übergeben werden. Beim Cookie-Ansatz liefern Angular-Dev-Proxy bzw. nginx same-origin, und SignalR sendet das Cookie automatisch mit.
- **Jobs pro Benutzer:** Für den Einsatz als kleines, gemeinsames Werkzeug nicht gewünscht. Eine spätere Umstellung ist möglich (Spalte `OwnerId` plus Filter).
- **Offene Registrierung:** Jeder, der die Seite erreicht, könnte sich selbst ein Konto anlegen. Der Schutz wäre damit wirkungslos.

## Konsequenzen

- Die Identity-Tabellen kommen per Migration dazu. Die Baseline für Datenbanken, die per `EnsureCreated` angelegt wurden (S04), bleibt gültig.
- Das Frontend braucht Login-Seite, Route-Guard und einen 401-Interceptor. SignalR verbindet sich erst nach dem Login.
- Integrationstests authentifizieren sich über einen Test-Auth-Handler.
- Cookie und `SameSite=Strict` setzen voraus, dass Frontend und API unter derselben Site laufen. Das ist über den Dev-Proxy bzw. nginx gegeben.
