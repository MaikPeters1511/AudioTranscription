# Story S27: Wiederaufnehmbarer Upload

> Epic: [E-02](./00-epic.openspec.md) · Phase 2 · Agenten: architect-agent, backend-agent, frontend-agent · Größe: L · Voraussetzung: [ADR 0006](../../docs/adr/0006-resumable-upload-spike.md), Entscheidung D9

## 1. Description
Bei Dateien bis 500 MB bricht ein instabiler Upload komplett ab. Ein wiederaufnehmbarer Upload setzt nach Verbindungsabbruch an der letzten bestätigten Stelle fort.

## 2. User Stories
- Als Benutzer möchte ich einen abgebrochenen Upload fortsetzen, damit ich große Dateien nicht von vorn hochladen muss.

## 3. Tasks

### S27-T1 ADR-Ergebnis festschreiben · architect · S
- **AC:** ADR 0006 enthält die Entscheidung (tus oder eigenes Chunking, D9) mit Begründung; offene Punkte (Reverse-Proxy-Limits, Ablauf unvollständiger Uploads) sind benannt.

### S27-T2 Backend: Upload-Sessions · backend · L
- Chunk-Annahme, Offset-Abfrage, Abschluss erzeugt den Job wie der bisherige Upload; Validierung (Größe, MIME, Magic Bytes) auf der zusammengesetzten Datei.
- **AC (TDD):**
  - [ ] Tests für Fortsetzen, doppelte Chunks, falschen Offset, Überschreiten des Größenlimits, unvollständige Sessions werden vom Cleanup entfernt (Bezug zu `OrphanedUploadCleanupService`).
  - [ ] Sitzungen sind an den Benutzer gebunden.

### S27-T3 Frontend: Chunk-Upload mit Wiederaufnahme · frontend · M
- **AC:** Fortschritt, Pause/Fortsetzen, automatischer Retry mit Backoff, Aufnahme (S12) nutzt denselben Weg; DE/EN, ARIA-Live.

### S27-T4 Infrastruktur · devops · S
- **AC:** nginx/Proxy-Konfiguration für Chunk-Größen, `docker-compose` und Aspire angepasst.

## 4. Acceptance Criteria (DoD)
- [ ] Ein bei 60 % unterbrochener Upload (E2E mit simuliertem Netzabbruch) wird fortgesetzt und erzeugt genau einen Job.
- [ ] Alter Upload-Endpoint bleibt für kleine Dateien erhalten oder ist bewusst abgelöst und dokumentiert.

## 5. Umsetzung

- **Entscheidung D9:** tus (`tusdotnet` 2.12.0, `tus-js-client` 4.3.1), festgehalten in [ADR 0006](../../docs/adr/0006-resumable-upload-spike.md), inklusive Antworten auf die offenen Fragen des Spikes.
- **Backend:** tus unter `/api/uploads` (`ResumableUploads`), Teil-Uploads im Unterordner `tus/` des Upload-Speichers. Eine Sitzung (`UploadSession`, Migration `AddUploadSessions`) bindet den Upload an den Benutzer; `POST`, `HEAD`, `PATCH`, `DELETE` und `GET .../job` verlangen Anmeldung und passenden Besitzer (fremd = 404). Größe, Content-Type und die Einstellungen (Modell, Sprache, Diarisierung, Glossar) werden schon beim Anlegen geprüft, die Magic Bytes an der fertigen Datei; danach wird sie nach `{jobId}{Endung}` verschoben und der Job genauso angelegt wie beim Multipart-Upload (gemeinsamer `UploadJobService`, der alte Endpoint nutzt ihn jetzt ebenfalls). Ein Abschluss erzeugt höchstens einen Job. `IncompleteUploadCleanupService` räumt unvollständige Sitzungen samt Teildatei nach `Upload:IncompleteSessionRetentionHours` ab.
- **Frontend:** `ResumableUploadService` (Chunks à 5 MiB, Retry-Verzögerungen 0/1/3/5/10/20 s, Wiederaufnahme einer bekannten Sitzung, Abbruch beendet die Sitzung auf dem Server). `AudioJobService.uploadFile` wählt ab 10 MiB (`RESUMABLE_UPLOAD_THRESHOLD_BYTES`) diesen Weg, darunter bleibt der einfache Upload; Einzel-Upload, Batch (S34) und Aufnahme (S12) nutzen dieselbe Weiche. Pause/Fortsetzen über `UploadControl` mit `aria-pressed` und `role="status"`-Anzeige „Pausiert“; DE/EN.
- **Infrastruktur:** `nginx.conf.template` hat eine eigene `location /api/uploads` (ungepuffert, 16 MiB je Request, `Host` samt Port); die `Location` der Sitzung ist relativ, damit sie hinter einem Proxy stimmt. `docker-compose.yml`/`.env.example`: `UPLOAD_INCOMPLETE_RETENTION_HOURS`. Aspire braucht keine Änderung (Dev-Proxy leitet `/api` komplett weiter).
- **Bewusste Abgrenzungen:** Der alte `POST /api/audio-jobs` bleibt für kleine Dateien und andere Clients bestehen. Geht die Antwort auf den allerletzten Chunk verloren, fragt der Client die Sitzung ab, findet sie nicht mehr (die Teildatei ist dann schon der Job) und legt einen neuen Upload an; das kann einen doppelten Job ergeben, der sich in der Liste löschen lässt. Die Mock-E2E (`resumable-upload.mocked.spec.ts`) läuft nur in der CI.
