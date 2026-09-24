# Story S16: CI mit GitHub Actions

> Epic: [E-01](./00-epic.openspec.md) · Phase 1 (vorgezogen) · Agent: devops-agent

## 1. Description
Es gibt keine CI. CLAUDE.md verlangt aber, dass PRs nur mit grünen Tests gemergt werden. Die Pipeline wird vorgezogen, damit alle weiteren Stories dieses Epics automatisch geprüft werden.

## 2. User Stories
- Als Entwickler möchte ich, dass jeder Push und jeder PR automatisch gebaut und getestet wird, damit Fehler vor dem Merge auffallen.

## 3. Tasks

### S16-T1 Backend-Job: build & test · devops · S
- `.github/workflows/ci.yml`, Trigger `push` und `pull_request`. Setup .NET 10, `dotnet restore`, `dotnet build --no-restore -warnaserror` (optional), `dotnet test --no-build`.
- Integrationstests, die SQL Server brauchen: Service-Container oder Testcontainers. Klären, was `AudioJobEndpointsIntegrationTests` aktuell benötigt.
- **AC:**
  - [ ] Workflow läuft auf einem PR grün durch.
  - [ ] Testergebnisse werden als Artefakt (TRX) hochgeladen.

### S16-T2 Frontend-Job: lint, build, unit tests · devops · S
- Node LTS, `npm ci`, `npm run build`, `npm test -- --watch=false` im Ordner `AudioTranscription.Web`.
- **AC:**
  - [ ] Ein Angular-Build-Fehler lässt den Workflow fehlschlagen.
  - [ ] npm-Cache ist aktiv.

### S16-T3 NuGet- und npm-Caching sowie Concurrency · devops · XS
- `actions/cache` bzw. `setup-*`-Cache, `concurrency`-Gruppe pro Branch, damit überholte Läufe abgebrochen werden.
- **AC:**
  - [ ] Ein zweiter Lauf ist messbar schneller als der erste.

### S16-T4 Artefakt-Guard (siehe S01-T4) · devops · XS
- Schritt, der fehlschlägt, wenn Audio- oder Modell-Dateien getrackt sind.
- **AC:**
  - [ ] Der Check ist Teil des Workflows.

### S16-T5 Playwright-E2E (optional, später) · qa · M
- E2E-Tests aus `AudioTranscription.Web/tests/` gegen eine per Aspire oder docker-compose gestartete Umgebung.
- **AC:**
  - [ ] Nightly- oder manueller Workflow läuft grün.

## 4. Acceptance Criteria (DoD)
- [ ] Branch-Protection auf `main` verlangt den CI-Check (Einstellung durch den Owner).
- [ ] README enthält ein CI-Badge.
