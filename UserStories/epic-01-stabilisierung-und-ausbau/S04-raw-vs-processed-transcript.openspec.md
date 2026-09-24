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
  - [ ] Die neue Migration benennt die Spalte um, bestehende Transkripte landen in `RawTranscript`.
  - [ ] Test: Migration lässt sich auf eine leere DB anwenden.

### S04-T2 Worker speichert beide Fassungen · backend · XS
- `RawTranscript = result.Text`; `ProcessedTranscript = postProcessor?.ProcessAsync(...)`, nur gesetzt, wenn das Ergebnis vom Rohtext abweicht.
- **AC (TDD):**
  - [ ] Test ohne Post-Processor: `ProcessedTranscript` ist `null`.
  - [ ] Test mit Post-Processor: Beide Felder sind gesetzt, `RawTranscript` ist unverändert.

### S04-T3 DTO und API anpassen · backend · XS
- **AC:**
  - [ ] Integrationstest für `GET /api/audio-jobs/{id}` prüft beide Felder.
  - [ ] Swagger ist aktuell.

### S04-T4 Frontend: Umschalter „Original / Bearbeitet“ · frontend · S
- In `job-detail.component.ts`: DaisyUI `tabs` bzw. `join`-Toggle mit `role="tablist"` und `aria-selected`, nur sichtbar, wenn `processedTranscript` vorhanden ist. Standardansicht ist „Bearbeitet“. Die Texte kommen aus i18n (EN-1).
- **AC:**
  - [ ] Per Tastatur umschaltbar (Pfeiltasten und Enter).
  - [ ] Unit-Test für beide Zustände.
  - [ ] Kopieren und Download nutzen die jeweils angezeigte Fassung.

## 5. Acceptance Criteria (DoD)
- [ ] Mit aktivem Ollama sind beide Fassungen abrufbar, ohne Ollama verhält sich die App wie bisher.
