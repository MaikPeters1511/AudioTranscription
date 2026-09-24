# ADR 0001: Ablageort von Tests

- **Status:** Akzeptiert
- **Datum:** 2026-09-24
- **Entscheider:** Repo-Owner
- **Kontext-Referenz:** Epic E-01, Entscheidung D5, Story S05-T2

## Kontext

CLAUDE.md schrieb bisher vor: „Alle Tests liegen zwingend im Ordner `Tests`.“ Der tatsächliche Stand wich davon ab:

- Die Backend-Tests (xUnit, Unit und Integration) lagen im Projekt `AudioTranscription.Tests/` im Repository-Root.
- Die Playwright-E2E-Tests lagen in `AudioTranscription.Web/tests/`. Sie nutzen `playwright.config.ts`, `node_modules` und das `package.json` des Web-Projekts.
- Der Ordner `Tests/` war leer.
- Frontend-Unit-Tests gibt es noch nicht (EN-2). Angular erwartet `*.spec.ts` standardmäßig neben dem getesteten Code in `src/`.

## Entscheidung

Mischform:

1. **Backend-Tests liegen unter `Tests/`.** Jedes .NET-Testprojekt ist ein eigener Unterordner, z. B. `Tests/AudioTranscription.Tests/`. Neue Testprojekte folgen dem Muster `Tests/<Projektname>/`.
2. **Frontend-Tests bleiben im Web-Projekt:**
   - Playwright-E2E-Tests in `AudioTranscription.Web/tests/`.
   - Angular-Unit-Tests (`*.spec.ts`) neben der getesteten Datei in `AudioTranscription.Web/src/`, nach Angular-Konvention.

## Begründung

- .NET-Testprojekte sind eigenständige Projekte. Die Verschiebung kostet nur angepasste `ProjectReference`- und Solution-Pfade, und `Tests/` bündelt alle Backend-Testprojekte an einer Stelle.
- Frontend-Tests hängen an der Toolchain des Web-Projekts (`angular.json`, `playwright.config.ts`, `node_modules`, `tsconfig.spec.json`). Außerhalb des Projekts bräuchten sie eigene Konfigurationen und Pfad-Mappings, ohne dass das einen Vorteil bringt.

## Konsequenzen

- `AudioTranscription.Tests/` ist nach `Tests/AudioTranscription.Tests/` verschoben. `AudioTranscription.slnx` und die `ProjectReference`s (`..\..\`) sind angepasst. Die Namespaces bleiben unverändert.
- CLAUDE.md, die Agenten-Definitionen (`.claude/agents/`) und die Skills (`.claude/skills/`) beschreiben die neue Regel.
- Die CI (`.github/workflows/ci.yml`) testet über die Solution und braucht keine Änderung.
