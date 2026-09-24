# Story S16: CI mit GitHub Actions

> Epic: [E-01](./00-epic.openspec.md) · GitHub: #5 · Phase 1 (vorgezogen) · Agent: devops-agent

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

## 4. Umsetzungsnotizen (2026-09-24)
- Workflow `.github/workflows/ci.yml` mit den Jobs `repo-hygiene` (T4), `backend` (T1) und `frontend` (T2). Caching und Concurrency (T3) sind enthalten. Trigger: `pull_request` und `push` auf `main`.
- **Kein `-warnaserror`:** Der AppHost erzeugt die Warnung `ASPIRE010` (`AspireUseCliBundle=false`).
- Die Integrationstests nutzen eine EF-Core-InMemory-DB, ein SQL-Server-Service-Container ist nicht nötig.
- **Node 24:** Die Angular CLI 22 verlangt Node ≥ 22.22.3 oder ≥ 24.15.
- **Frontend-Unit-Tests fehlen:** `angular.json` hat kein `test`-Target und es gibt keine `*.spec.ts`. `ng test` schlägt deshalb fehl. Die Einrichtung ist als Enabler EN-2 ausgelagert. Sobald sie steht, kommt der Schritt `npm test` in den Job `frontend`.
- **Kein Prettier-Check:** 12 bestehende Dateien sind nicht Prettier-konform. Ein Check würde sofort fehlschlagen. Er wird ergänzt, sobald die Dateien einmal formatiert wurden.

## 5. Acceptance Criteria (DoD)
- [ ] Branch-Protection auf `main` verlangt den CI-Check (Einstellung durch den Owner).
- [ ] README enthält ein CI-Badge.
