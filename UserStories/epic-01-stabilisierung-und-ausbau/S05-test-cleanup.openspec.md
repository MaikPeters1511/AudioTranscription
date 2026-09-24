# Story S05: Platzhalter-Test entfernen

> Epic: [E-01](./00-epic.openspec.md) · Phase 1 · Agenten: backend-agent, qa-agent

## 1. Description
`AudioTranscription.Tests/UnitTest1.cs` enthält einen leeren `[Fact]`, der immer grün ist und die Testabdeckung verfälscht.

## 2. Tasks

### S05-T1 `UnitTest1.cs` löschen · backend · XS
- **AC:**
  - [ ] Die Datei ist entfernt, `dotnet test` ist grün.

### S05-T2 Test-Ablageort klären (Entscheidung D5) · architect · XS
- CLAUDE.md verlangt `Tests/`, tatsächlich liegen die Tests in `AudioTranscription.Tests/` und `AudioTranscription.Web/tests/`. Kurz-ADR: Ist-Zustand übernehmen oder Projekte nach `Tests/` verschieben.
- **AC:**
  - [ ] ADR in `docs/adr/` liegt vor, CLAUDE.md oder die Projektstruktur ist angepasst.

## 3. Acceptance Criteria (DoD)
- [ ] Kein leerer oder trivialer Test mehr im Repo.
