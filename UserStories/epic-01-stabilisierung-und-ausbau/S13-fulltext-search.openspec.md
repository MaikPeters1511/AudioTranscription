# Story S13: Volltextsuche über alle Transkripte

> Epic: [E-01](./00-epic.openspec.md) · GitHub: #17 · Phase 3 · Agenten: architect-agent, backend-agent, frontend-agent · Voraussetzung: D1 (Datenbank)

## 1. Description
Nutzer sollen alle Transkripte nach Begriffen durchsuchen und direkt zur Fundstelle springen können. Mit den Segmenten aus S06 ist das bis auf den Zeitstempel genau möglich.

## 2. User Stories
- Als Nutzer möchte ich nach einem Begriff suchen und alle Transkripte mit Fundstellen sehen, damit ich eine bestimmte Aussage schnell wiederfinde.

## 3. API & Backend Requirements
- `GET /api/search?q=...&page=&pageSize=` → Treffer mit `jobId`, `fileName`, `snippet`, optional `segmentStartMs`.
- Suche über `RawTranscript`, `ProcessedTranscript` und Segmente.

## 4. Tasks

### S13-T1 ADR: Suchtechnologie · architect · S
- Hängt von D1 ab. Bei SQL Server: Full-Text Index (`CONTAINS`/`FREETEXT`), der Container braucht die FTS-Komponente. Bei PostgreSQL: `tsvector` mit GIN-Index und Sprachkonfiguration `german`/`english`. Alternative: semantische Suche über Qdrant laut Tech-Stack (eigene Story).
- **AC:**
  - [ ] ADR in `docs/adr/`.

### S13-T2 Index-Migration · backend · S
- **AC:**
  - [ ] Migration ist vorhanden.
  - [ ] Der Index funktioniert im Aspire- und docker-compose-Setup.

### S13-T3 Such-Query und Endpoint · backend · M
- Eingabe validieren und escapen (keine Injection über die FTS-Syntax), paginieren, Snippet mit Treffer-Hervorhebung serverseitig als Offsets liefern, nicht als HTML.
- **AC (TDD):**
  - [ ] Integrationstests gegen eine echte DB (Testcontainers) für Treffer, keine Treffer, Sonderzeichen und Paginierung.

### S13-T4 Frontend: Suchfeld und Trefferliste · frontend · M
- `<search>`-Landmark und `input type="search"` mit Label, Debounce. Treffer verlinken auf das Job-Detail und springen, falls ein Segment vorhanden ist, an die Stelle im Player (S06).
- **AC:**
  - [ ] Unit-Tests für Debounce, Leerzustand und Treffer-Rendering.
  - [ ] Hervorhebung per `<mark>`.

## 5. Acceptance Criteria (DoD)
- [ ] Die Suche nach einem Wort aus einer deutschen Aufnahme findet sie auch in gebeugter Form (Stemming), sofern die gewählte Technologie das unterstützt.
