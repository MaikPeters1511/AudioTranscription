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

## 6. Umsetzung

- **Entscheidungen:** [ADR 0009](../../docs/adr/0009-automatisierung-api-keys-webhooks-ordner-watch.md) (Key-Format und Hashing, Scopes, SSRF-Regeln, Signatur, Wiederholung, Ordner-Watch).
- **API-Keys:** `at_<Präfix>_<Geheimnis>` (8 Zeichen öffentlich, 256 Bit geheim), nur der SHA-256-Hash liegt in der Tabelle `ApiKeys` (Migration `AddAutomation`), Vergleich zeitkonstant. `Authorization: Bearer` oder `X-Api-Key`; eigenes Auth-Schema `ApiKey` hinter einem Policy-Schema, die Cookie-Sitzung bleibt unverändert. Scopes `jobs:read`/`jobs:write` und die Pfad-Allowlist (`/api/audio-jobs`, `/api/uploads`, `/api/search`) erzwingt eine einzige Middleware (`ApiKeyScopeMiddleware`); Keys verwalten sich nicht selbst. Abgelaufen, entzogen, unbekannt, Benutzer deaktiviert: `401`; `LastUsedAt` höchstens einmal je Minute; der Key steht in keinem Log (Test mit aufzeichnendem Logger); Rate-Limit pro Key (`429`).
- **Webhooks:** Outbox-Tabelle `WebhookDeliveries` (Zustellungsprotokoll und Warteschlange in einem), `WebhookDeliveryService` sendet fällige Zeilen; Wiederholung nach 30 s, 2 min, 10 min, 1 h, 6 h, 4xx (außer 408/429) endgültig. Signatur `v1=` + HMAC-SHA256 über `Timestamp.Body`, Geheimnis mit Data Protection verschlüsselt. Nutzdaten ohne Transkript und Fehlertext. SSRF: URL-Prüfung beim Speichern und Zustellen, Adressprüfung beim Verbindungsaufbau (DNS-Rebinding), keine Weiterleitungen, 10 s Timeout, eigener HTTP-Client ohne die Resilienz-/Service-Discovery-Defaults. `POST /api/webhooks/{id}/test` (10 pro Minute).
- **Ordner-Watch:** `Watch:Folders[]` (`Path`, `User`, optional `Model`/`Language`), Abfrage statt `FileSystemWatcher`; nur Dateien direkt im Ordner, keine Symlinks/versteckten Dateien, Endungs-Allowlist plus Magic Bytes, `Upload:MaxFileSizeBytes`, Stabilitätsfenster; Original wandert nach `processed/` bzw. `rejected/`. Neustartsicher über eine aus der Datei abgeleitete Job-ID.
- **Frontend:** Seiten `/api-keys` und `/webhooks` (Key/Geheimnis einmalig mit Kopierfunktion, Zustellungsverlauf, Test-Knopf mit `role="status"`, Bestätigung vor Löschen/Entziehen), DE/EN, beschriftete Schaltflächen.
- **Swagger:** Die OpenAPI-Beschreibung enthält die Schemata `SessionCookie`, `ApiKey` und `BearerApiKey`; README enthält `curl`- und n8n-Beispiele.
- **Betrieb:** `DataProtection:KeysPath` (docker-compose: Volume `api-dataprotection`), `Webhooks:AllowPrivateTargets`/`AllowHttp` als Betreiberschalter.
- **Bewusste Abgrenzungen:** Webhook-Geheimnisse lassen sich nicht rotieren (neu anlegen und alten löschen); Zustellung ist „mindestens einmal“ (Empfänger dedupliziert über `X-Webhook-Id`); der Ordner-Watch gehört auf eine API-Instanz und liest Dateien nicht gegen ein Austauschen zwischen Prüfung und Kopie (Schreibzugriff auf den Ordner ist Betreiberrecht). Der Security-Review nach DoD ist als Selbstprüfung gegen die Checkliste im PR dokumentiert; eine unabhängige Abnahme durch den `sec-agent` steht aus.

