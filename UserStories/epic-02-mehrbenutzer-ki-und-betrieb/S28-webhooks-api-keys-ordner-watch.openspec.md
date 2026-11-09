# Story S28: Webhooks, API-Keys und Ordner-Watch

> Epic: [E-02](./00-epic.openspec.md) · Phase 3 · Agenten: architect-agent, sec-agent, backend-agent · Größe: L · Voraussetzung: S19

## 1. Description
Automatisierung: Ein „Fertig“-Event für Werkzeuge wie n8n oder Zapier, API-Keys für Skripte und ein überwachter Ordner („Datei ablegen, Transkript erhalten“).

## 2. User Stories
- Als Benutzer möchte ich per Webhook benachrichtigt werden, wenn ein Transkript fertig ist, damit ich Folgeprozesse automatisiere.
- Als Entwickler möchte ich per API-Key auf die API zugreifen, damit Skripte ohne Browser-Login arbeiten.
- Als Admin möchte ich einen Ordner überwachen lassen, damit abgelegte Dateien automatisch transkribiert werden.

## 3. API & Backend Requirements
- **Modelle:** `ApiKey` (Hash, Präfix, Scopes, OwnerId, ExpiresAt, LastUsedAt), `Webhook` (Url, Secret, Events, OwnerId), `WebhookDelivery` (Status, Versuche).
- **Endpoints:** `POST/GET/DELETE /api/api-keys` (Key wird nur einmal angezeigt), `POST/GET/PUT/DELETE /api/webhooks`, `POST /api/webhooks/{id}/test`.
- **Webhook-Signatur:** HMAC-SHA256 im Header, Retry mit Backoff.
- **Ordner-Watch:** Konfiguration `Watch:Folders[]` mit Zielbenutzer; verarbeitete Dateien werden verschoben.

## 4. Tasks

### S28-T1 ADR: Auth-Schema für API-Keys und SSRF-Schutz · architect, sec · S
- **AC:** ADR legt Key-Format, Hashing (nur Hash gespeichert), Scopes und SSRF-Regeln für Webhook-Ziele fest (keine internen Adressen/Link-Local, Redirect-Limit, Timeout).

### S28-T2 API-Keys · backend, sec · M
- **AC (TDD):** Authentifizierung per `Authorization: Bearer`/Header, abgelaufene/entzogene Keys → 401, Scopes erzwungen, Key taucht nie in Logs auf.

### S28-T3 Webhooks · backend · M
- **AC (TDD):** Signatur korrekt, Retry mit Backoff, Zustellungsprotokoll, SSRF-Tests blockieren interne Ziele.

### S28-T4 Ordner-Watch · backend, devops · M
- **AC:** Nur Dateien mit erlaubtem Typ, Größenlimit, Pfad-Traversal ausgeschlossen, Neustart-sicher (keine Doppelverarbeitung).

### S28-T5 Frontend: Verwaltung · frontend, ux · M
- **AC:** Seiten für API-Keys und Webhooks, Key einmalig mit Kopierfunktion, Zustellungsverlauf, DE/EN, ARIA.

## 5. Acceptance Criteria (DoD)
- [ ] Security-Review durch `sec-agent` bestanden (SSRF, Secret-Handling, Rate-Limit).
- [ ] Beispiel für n8n/curl in der README, Swagger dokumentiert Auth-Schemata.
