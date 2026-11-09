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
