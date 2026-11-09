# Story S31: E2E-Tests für die Kern-Flows

> Epic: [E-02](./00-epic.openspec.md) · Phase 1 · Agenten: qa-agent · Größe: M

## 1. Description
Vor dem Umbau auf Mehrbenutzer (S18/S19) brauchen wir ein Sicherheitsnetz. Playwright-Tests (`AudioTranscription.Web/tests/`, siehe ADR 0001) decken die wichtigsten Nutzerwege ab und laufen in der CI.

## 2. User Stories
- Als Entwickler möchte ich, dass die Kern-Flows automatisiert End-to-End geprüft werden, damit Umbauten keine Regressionen unbemerkt einführen.

## 3. Vorgehen (gestaffelt)
- **Stufe 1 (umgesetzt):** UI-Flows gegen eine gefakte API (`tests/support/mock-api.ts`, `*.mocked.spec.ts`), laufen mit dem Angular-Dev-Server allein, eigener CI-Job `Frontend E2E (mocked API)`.
- **Stufe 2 (offen):** dieselben Flows gegen den echten Stack (docker-compose: API, SQL Server, Frontend) als zweiter CI-Job.

## 3a. Tasks

### S31-T1 Testumgebung und Fixtures · qa · S
- Stabile Testdaten (kurze generierte WAV-Datei), Test-Login über Seeder-Konfiguration, ein Whisper-Modell oder ein Stub für schnelle Läufe.
- **AC:**
  - [ ] `npx playwright test` läuft lokal und in der CI gegen eine frisch gestartete Umgebung.
  - [ ] Keine echten Zugangsdaten im Repo, nur Test-Werte aus Beispiel-Konfiguration.

### S31-T2 Flow: Login und Logout · qa · XS
- **AC:**
  - [ ] Erfolgreicher Login, fehlgeschlagener Login (saubere Fehlermeldung), Logout, geschützte Route leitet auf Login um.

### S31-T3 Flow: Upload bis Transkript · qa · S
- **AC:**
  - [ ] Datei hochladen, Live-Fortschritt erscheint, fertiges Transkript wird angezeigt, Kopieren und `.txt`-Download funktionieren.

### S31-T4 Flow: Suche und Sprungmarke · qa · S
- **AC:**
  - [ ] Suchbegriff liefert Treffer mit `<mark>`, Klick öffnet Detailansicht und springt im Player.

### S31-T5 Flow: Sprecher umbenennen und Job löschen · qa · S
- **AC:**
  - [ ] Sprecher umbenennen wirkt sich auf die Anzeige und den SRT-Export aus; Job löschen entfernt ihn aus der Liste.

### S31-T6 CI-Integration und Report · devops, qa · S
- **AC:**
  - [ ] Eigener CI-Job, HTML-Report und Traces bei Fehlern als Artefakt, Flaky-Test-Retry höchstens 1.

## 4. Acceptance Criteria (DoD)
- [ ] Alle fünf Flows sind grün in der CI auf `main`.
- [ ] Tests sind unabhängig voneinander und räumen ihre Daten auf.
