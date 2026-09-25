# Story S05: Platzhalter-Test entfernen

> Epic: [E-01](./00-epic.openspec.md) · GitHub: #6 · Phase 1 · Agenten: backend-agent, qa-agent

## 1. Description
`AudioTranscription.Tests/UnitTest1.cs` enthält einen leeren `[Fact]`, der immer grün ist und die Testabdeckung verfälscht.

## 2. Tasks

### S05-T1 `UnitTest1.cs` löschen · backend · XS
- **AC:**
  - [x] Die Datei ist entfernt, `dotnet test` ist grün.
- Zusätzlich entfernt: `AudioTranscription.Web/tests/example.spec.ts` (Playwright-Beispiel aus der Projektvorlage, testet `playwright.dev`).

### S05-T2 Test-Ablageort klären (Entscheidung D5) · architect · XS
- CLAUDE.md verlangt `Tests/`, tatsächlich liegen die Tests in `AudioTranscription.Tests/` und `AudioTranscription.Web/tests/`. Kurz-ADR: Ist-Zustand übernehmen oder Projekte nach `Tests/` verschieben.
- **AC:**
  - [x] ADR in `docs/adr/` liegt vor, CLAUDE.md oder die Projektstruktur ist angepasst.
- **Entscheidung (2026-09-24):** Mischform, siehe [ADR 0001](../../docs/adr/0001-ablageort-von-tests.md). Backend-Tests liegen unter `Tests/<Projekt>/`, die Frontend-Tests bleiben im Web-Projekt.

## 3. Acceptance Criteria (DoD)
- [x] Kein leerer oder trivialer Test mehr im Repo.
