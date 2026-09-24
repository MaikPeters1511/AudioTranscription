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
  - [x] ADR in `docs/adr/`: [ADR 0005](../../docs/adr/0005-volltextsuche.md). D1 entschieden (bei SQL Server bleiben), Technologie: SQL Server Full-Text Search gegenüber Qdrant. Nach Implementierung korrigiert: `FREETEXT` statt `CONTAINS` (siehe T3), und die FTS-Komponente ist im Standard-Image entgegen der ersten Annahme **nicht** enthalten (siehe T2).

### S13-T2 Index-Migration · backend · S
- **AC:**
  - [x] Migration ist vorhanden: `AddFullTextSearchIndex` (Katalog + drei Volltextindizes auf `AudioJobs.RawTranscript`, `TranscriptSegments.Text`, `TranscriptVariants.Text`), mit `IF SERVERPROPERTY('IsFullTextInstalled') = 1`-Absicherung, damit sie auf einer Datenbank ohne FTS-Komponente als No-Op durchläuft statt fehlzuschlagen (betrifft alle anderen, nicht-Such-bezogenen Testcontainers-Tests dieses Repos).
  - [~] Der Index funktioniert im Aspire- und docker-compose-Setup: Beide sind auf ein neues Image `docker/mssql-fts` (Aspire: `.WithDockerfile("../docker/mssql-fts")`, docker-compose: `build: ./docker/mssql-fts`) statt des Standard-`mssql/server`-Images umgestellt, das per `apt-get install mssql-server-fts` die fehlende Komponente nachinstalliert. **Konnte in dieser Sandbox nicht gebaut/verifiziert werden** — `packages.microsoft.com` ist per TLS-Zertifikatsfehler gesperrt (siehe ADR 0005, Umsetzungsnotizen). Ein Betreiber mit Internetzugang (z. B. die GitHub-Actions-CI dieses Repos) muss den Image-Build und damit den vollständigen End-to-End-Pfad verifizieren.

### S13-T3 Such-Query und Endpoint · backend · M
- Eingabe validieren und escapen (keine Injection über die FTS-Syntax), paginieren, Snippet mit Treffer-Hervorhebung serverseitig als Offsets liefern, nicht als HTML.
- **AC (TDD):**
  - [~] Integrationstests gegen eine echte DB (Testcontainers) für Treffer, keine Treffer, Sonderzeichen und Paginierung: `SearchIntegrationTests` (10 Tests) sind geschrieben und decken alle geforderten Fälle ab (Treffer in `RawTranscript`/Segment/Variante, keine Treffer, drei FTS-Sonderzeichen-Payloads inkl. eines SQL-Injection-Versuchs, fehlende Query → 400, Paginierung, Stemming). **Können in dieser Sandbox nicht ausgeführt werden**, da sie dasselbe FTS-fähige Testcontainers-Image aus T2 brauchen (siehe dort). Die reine Snippet-/Offset-Logik (`SearchSnippetBuilder`, 7 Tests, mutationsgetestet) läuft unabhängig davon grün.
- Abweichung von der ursprünglichen Formulierung: `EF.Functions.FreeText` statt `CONTAINS` mit manuellem Phrasen-Escaping (siehe ADR 0005) — `FREETEXT` hat keine Operator-Syntax, die Nutzereingaben injizieren könnten, und liefert das DoD-Stemming direkt mit, ganz ohne `FORMSOF`-Sonderfälle.

### S13-T4 Frontend: Suchfeld und Trefferliste · frontend · M
- `<search>`-Landmark und `input type="search"` mit Label, Debounce. Treffer verlinken auf das Job-Detail und springen, falls ein Segment vorhanden ist, an die Stelle im Player (S06).
- **AC:**
  - [x] Unit-Tests für Debounce, Leerzustand und Treffer-Rendering (`search.component.spec.ts`, 8 Tests; `search.service.spec.ts`, 3 Tests; `TranscriptPlayerComponent`-Erweiterung, 2 Tests für den Deep-Link).
  - [x] Hervorhebung per `<mark>`, aus den vom Backend gelieferten Offsets gebaut (kein HTML vom Server, siehe ADR 0005); ein Treffer ohne literalen Treffer (nur gebeugte Form) zeigt keinen `<mark>`.
- Umsetzung: neue Route `/search` (`SearchComponent`) mit Debounce (300 ms), `SearchService`. Ein Treffer mit `segmentStartMs` verlinkt auf `/jobs/{id}?t={ms}`; `JobDetailComponent` liest den Query-Parameter `t` und reicht ihn als `initialSeekMs` an `TranscriptPlayerComponent` durch, der die Audiowiedergabe und die aktive Segment-Markierung entsprechend springen lässt, sobald Segmente und das `<audio>`-Element geladen sind. Manuell im Browser mit echtem `<audio>`-Element (kein Mock) verifiziert: Playwright mit einer generierten WAV-Datei bestätigte, dass `audio.currentTime` tatsächlich springt und das richtige Segment als aktiv markiert wird (ein erster Versuch scheiterte an einer statischen Test-Datei ohne `Accept-Ranges`-Header, was Chromiums Seek stillschweigend verwarf — kein Produktcode-Fehler, nach Korrektur der Testfixture bestätigt).

## 5. Acceptance Criteria (DoD)
- [~] Die Suche nach einem Wort aus einer deutschen Aufnahme findet sie auch in gebeugter Form (Stemming), sofern die gewählte Technologie das unterstützt. `FREETEXT` mit `LANGUAGE 1031` (Deutsch) unterstützt das; ein `SearchIntegrationTests`-Test (`Search_SupportsGermanStemming`) prüft das explizit, konnte aber aus demselben Sandbox-Grund wie T2/T3 hier nicht ausgeführt werden.
