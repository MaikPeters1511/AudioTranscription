# Epic E-01: Stabilisierung & Ausbau der Audio-Transkription

> GitHub: Epic #2, Stories #4–#19, Tasks #20–#92 (als Sub-Issues verknüpft)

## 1. Description
AudioTranscription wirbt mit „privat & offline“. Die Analyse des aktuellen Stands (Commit `9b0a5d9`) zeigt Lücken bei Datenschutz, Robustheit und Nutzwert:

- Hochgeladene Audiodateien liegen im Git-Verlauf, eine `.gitignore` im Root fehlt.
- Jobs gehen bei einem Neustart verloren (`TranscriptionQueue` ist ein reiner In-Memory-Channel).
- Temp-Dateien bleiben nach Fehlern liegen, die Ollama-Nachbearbeitung überschreibt das Roh-Transkript.
- Whisper liefert Zeitstempel pro Segment, der Code verwirft sie.
- Modell (`GgmlType.Base`) und Sprache (Auto-Erkennung) sind fest verdrahtet.

Das Epic bündelt 16 Stories in drei Phasen. Jede Story ist in kleine Tasks (XS–M, höchstens ein Tag) zerlegt, die einzeln per PR umsetzbar sind.

**Ziel:** Nach Phase 1 ist das System datenschutzkonform und robust. Nach Phase 2 bietet es Untertitel, Live-Fortschritt, Konfigurierbarkeit und Job-Verwaltung. Phase 3 enthält größere Ausbaustufen, die erst nach Spikes oder ADRs umgesetzt werden.

## 2. Stories

### Phase 1: Aufräumen & Absichern (Sprint 1)
| ID | Issue | Story | Agent(s) | Größe |
|----|-------|-------|----------|-------|
| [S01](./S01-repo-hygiene.openspec.md) | #4 | Repo-Hygiene: `.gitignore` anlegen, Audiodateien entfernen | devops, sec | S |
| [S16](./S16-ci-pipeline.openspec.md) | #5 | CI mit GitHub Actions (vorgezogen als Sicherheitsnetz) | devops | S |
| [S05](./S05-test-cleanup.openspec.md) | #6 | Platzhalter-Test entfernen | backend, qa | XS |
| [S03](./S03-temp-file-cleanup.openspec.md) | #7 | Temp-Dateien auch nach Fehlern löschen | backend | S |
| [S02](./S02-job-recovery.openspec.md) | #8 | Offene Jobs beim Start wiederherstellen | backend | M |
| [S04](./S04-raw-vs-processed-transcript.openspec.md) | #9 | Roh- und nachbearbeitetes Transkript getrennt speichern | backend, frontend | M |

### Phase 2: Features mit viel Wirkung (Sprint 2–3)
| ID | Issue | Story | Agent(s) | Größe |
|----|-------|-------|----------|-------|
| [S17](./S17-authentication.openspec.md) | #94 | Authentifizierung (lokale Identity), Voraussetzung für S09 | sec, backend, frontend | L |
| [S09](./S09-job-lifecycle.openspec.md) | #10 | Jobs löschen, abbrechen, neu starten | backend, frontend, sec | L |
| [S08](./S08-model-and-language.openspec.md) | #11 | Modell und Sprache wählbar | backend, frontend | M |
| [S07](./S07-live-progress.openspec.md) | #12 | Live-Fortschritt in Prozent | backend, frontend | S |
| [S06](./S06-subtitles-and-player.openspec.md) | #13 | Segmente speichern, SRT/VTT-Export, synchroner Player | backend, frontend, ux | L |
| [S10](./S10-postprocessing-modes.openspec.md) | #14 | Mehrere Nachbearbeitungs-Modi mit Ollama | ai, backend, frontend | M |

### Phase 3: Größere Ausbaustufen (ab Sprint 4, Spike/ADR zuerst)
| ID | Issue | Story | Agent(s) | Größe |
|----|-------|-------|----------|-------|
| [S14](./S14-large-files-and-video.openspec.md) | #15 | Große Dateien & Video-Upload | backend, devops, frontend | L |
| [S12](./S12-browser-recording.openspec.md) | #16 | Direkt im Browser aufnehmen | frontend, backend, ux | M |
| [S13](./S13-fulltext-search.openspec.md) | #17 | Volltextsuche über alle Transkripte | architect, backend, frontend | M |
| [S15](./S15-gpu-acceleration.openspec.md) | #18 | GPU-Beschleunigung (CUDA/CoreML) | devops, backend | M |
| [S11](./S11-speaker-diarization.openspec.md) | #19 | Sprechererkennung | architect, ai, backend, frontend | XL |

**Reihenfolge:** CI (S16) wird vorgezogen, damit jede folgende Änderung automatisch geprüft wird. S04 und S06 ändern beide das Datenmodell und sollten deshalb nacheinander umgesetzt werden, nicht parallel.

## 3. Übergreifende Voraussetzungen (Enabler)
- **EN-1 i18n-Grundgerüst im Frontend (#3):** ✅ Umgesetzt mit Transloco, siehe [ADR 0002](../../docs/adr/0002-frontend-i18n-mit-transloco.md). `AudioTranscription.Web` hat aktuell keine i18n-Infrastruktur. Laut CLAUDE.md sind hartcodierte sichtbare Texte untersagt. Das Grundgerüst (DE/EN) muss deshalb vor der ersten Frontend-Task dieses Epics stehen. *Agent: frontend · Größe: M*
- **EN-2 Frontend-Unit-Test-Infrastruktur (#93):** Im Web-Projekt fehlte ein `test`-Target, `ng test` schlug fehl (aufgefallen bei S16). ✅ Umgesetzt: `@angular/build:unit-test` mit Vitest 4 und jsdom, erste Specs, CI-Schritt. *Agent: frontend · Größe: S*

## 4. Offene Entscheidungen (vor Sprint-Start klären)
| # | Frage | Betrifft | Empfehlung |
|---|-------|----------|------------|
| D1 | **Datenbank:** Der Code nutzt SQL Server (`AddSqlServer`, `UseSqlServer`), CLAUDE.md nennt PostgreSQL. Welche gilt? | S13 (Volltextsuche), alle Migrationen | ✅ Entschieden (2026-09-24): **Bei SQL Server bleiben**, siehe [ADR 0005](../../docs/adr/0005-volltextsuche.md). |
| D2 | **Audio-Aufbewahrung:** Player (S06) und Retry (S09) brauchen die Originaldatei. `DeleteAfterTranscription=true` löscht sie aber. | S03, S06, S09 | ✅ Entschieden (2026-09-24): Nach Erfolg wird gelöscht. Bei `Failed`/`Cancelled` bleibt die Datei für einen Neustart liegen, bis der Job gelöscht wird oder die Frist `OrphanedFileRetentionHours` abläuft (Umsetzung in S09). Der Player (S06) wird später entschieden. |
| D3 | **Git-History bereinigen:** Sollen die Audiodateien auch aus dem Verlauf entfernt werden? Das erfordert einen Force-Push. | S01 | ✅ Entschieden (2026-09-24): **nicht bereinigen**, siehe S01-T3 |
| D4 | **Authentifizierung:** Alle Endpoints sind anonym. Mit `DELETE` (S09) könnte jeder beliebige Jobs löschen. | S09, S10 | ✅ Entschieden (2026-09-24): **Erst Auth, dann S09.** Lokale ASP.NET Core Identity, gemeinsame Jobs für alle Angemeldeten, siehe [S17](./S17-authentication.openspec.md). |
| D5 | **Test-Ablage:** CLAUDE.md schreibt `Tests/` vor, Backend-Tests liegen in `AudioTranscription.Tests/`, E2E-Tests in `AudioTranscription.Web/tests/`. | alle | ✅ Entschieden (2026-09-24): Mischform, siehe [ADR 0001](../../docs/adr/0001-ablageort-von-tests.md) |

## 5. Beobachtungen außerhalb des Scopes
- `WhisperTranscriptionService` enthält ein leeres `catch { /* best effort */ }`. Das verstößt gegen CLAUDE.md und wird in S03-T2 behoben.
- CORS erlaubt jede Origin zusammen mit `AllowCredentials` (`Program.cs`). Das sollte `sec-agent` separat bewerten.

## 6. Acceptance Criteria (Epic-DoD)
- [ ] Alle Stories aus Phase 1 und 2 sind abgenommen, Phase-3-Stories haben mindestens ein abgeschlossenes Spike- oder ADR-Ergebnis.
- [ ] CI (S16) ist auf `main` grün.
- [ ] Keine Nutzerdaten (Audio, Transkripte) im Repository oder im Git-Verlauf (sofern D3 entschieden).
- [ ] Alle neuen UI-Texte liegen in DE/EN vor, alle neuen Komponenten sind per Tastatur bedienbar und haben ARIA-Labels.
- [ ] Swagger/OpenAPI dokumentiert alle neuen Endpoints (`doc-agent`).
