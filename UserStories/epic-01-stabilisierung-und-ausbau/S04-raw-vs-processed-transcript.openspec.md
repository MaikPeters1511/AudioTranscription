# Story S04: Roh- und nachbearbeitetes Transkript getrennt speichern

> Epic: [E-01](./00-epic.openspec.md) · GitHub: #9 · Phase 1 · Agenten: backend-agent, frontend-agent, ai-agent

## 1. Description
Der `TranscriptionWorker` schreibt das Ergebnis der Ollama-Nachbearbeitung in `TranscriptText` und verwirft den Whisper-Rohtext. Verfälscht das LLM Inhalte, lässt sich das Original nicht wiederherstellen. Künftig werden beide Fassungen gespeichert. Diese Story ist außerdem die Grundlage für S10 (mehrere Modi).

## 2. User Stories
- Als Nutzer möchte ich jederzeit das unveränderte Whisper-Transkript sehen, damit ich der Nachbearbeitung nicht blind vertrauen muss.

## 3. API & Backend Requirements
- **Modell `AudioJob`:** `TranscriptText` → `RawTranscript` (string, nullable); neu `ProcessedTranscript` (string, nullable).
- **Migration:** Umbenennung der Spalte, damit bestehende Daten erhalten bleiben (`RenameColumn`), keine Drop-und-Add-Migration.
- **DTO:** `AudioJobDto` liefert `rawTranscript` und `processedTranscript`. Das bisherige Feld `transcriptText` wird entfernt, ein Breaking Change im Frontend-Modell.

## 4. Tasks

### S04-T1 Domain-Modell und Migration · backend · S
- **AC:**
  - [x] Die neue Migration benennt die Spalte um, bestehende Transkripte landen in `RawTranscript`.
  - [x] Test: Migration lässt sich auf eine leere DB anwenden.

### S04-T2 Worker speichert beide Fassungen · backend · XS
- `RawTranscript = result.Text`; `ProcessedTranscript = postProcessor?.ProcessAsync(...)`, nur gesetzt, wenn das Ergebnis vom Rohtext abweicht.
- **AC (TDD):**
  - [x] Test ohne Post-Processor: `ProcessedTranscript` ist `null`.
  - [x] Test mit Post-Processor: Beide Felder sind gesetzt, `RawTranscript` ist unverändert.

### S04-T3 DTO und API anpassen · backend · XS
- **AC:**
  - [x] Integrationstest für `GET /api/audio-jobs/{id}` prüft beide Felder.
  - [ ] Swagger ist aktuell.

### S04-T4 Frontend: Umschalter „Original / Bearbeitet“ · frontend · S
- In `job-detail.component.ts`: DaisyUI `tabs` bzw. `join`-Toggle mit `role="tablist"` und `aria-selected`, nur sichtbar, wenn `processedTranscript` vorhanden ist. Standardansicht ist „Bearbeitet“. Die Texte kommen aus i18n (EN-1).
- **AC:**
  - [ ] Per Tastatur umschaltbar (Pfeiltasten und Enter).
  - [ ] Unit-Test für beide Zustände.
  - [ ] Kopieren und Download nutzen die jeweils angezeigte Fassung.

## 5. Umsetzungsnotizen (2026-09-24)
- **Voraussetzung umgesetzt (Entscheidung Repo-Owner: Option 1 mit Baseline):** Die API ruft beim Start `MigrateWithBaselineAsync` statt `EnsureCreatedAsync` auf. Datenbanken, die per `EnsureCreated` angelegt wurden (Tabellen vorhanden, aber kein `__EFMigrationsHistory`), bekommen `InitialCreate` als angewendet eingetragen. Danach laufen die ausstehenden Migrationen. Tests laufen gegen einen echten SQL Server (Testcontainers, Docker erforderlich).
- **T1:** Die Migration `SplitRawAndProcessedTranscript` nutzt `RenameColumn` (`TranscriptText` → `RawTranscript`) und `AddColumn` (`ProcessedTranscript`). Ein Test belegt, dass bestehende Transkripte dabei erhalten bleiben.
- **T2:** `ProcessedTranscript` wird nur gesetzt, wenn der Post-Processor einen nicht-leeren, geänderten Text liefert.
- **T3:** Das Frontend-Modell ist angepasst. Bis T4 zeigt die Detailansicht wie bisher die bearbeitete Fassung, falls vorhanden, sonst den Rohtext.
- **T4 offen:** Der Umschalter braucht neue UI-Texte und damit das i18n-Grundgerüst (EN-1, #3). Für seine Unit-Tests fehlt außerdem die Frontend-Test-Infrastruktur (EN-2, #93).

## 6. Acceptance Criteria (DoD)
- [ ] Mit aktivem Ollama sind beide Fassungen abrufbar, ohne Ollama verhält sich die App wie bisher.
