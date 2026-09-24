# Story S10: Mehrere Nachbearbeitungs-Modi mit Ollama

> Epic: [E-01](./00-epic.openspec.md) · GitHub: #14 · Phase 2 · Agenten: ai-agent, backend-agent, frontend-agent · Voraussetzung: S04

## 1. Description
`OllamaPostProcessor` hat einen fest eingebauten „Aufräumen“-Prompt. Aus demselben Transkript lassen sich weitere nützliche Ergebnisse erzeugen: eine Zusammenfassung, Stichpunkte, Aufgaben aus einem Meeting oder eine Übersetzung. Die Ergebnisse werden als eigene Varianten gespeichert und überschreiben weder den Rohtext noch einander.

## 2. User Stories
- Als Nutzer möchte ich aus einem fertigen Transkript per Klick eine Zusammenfassung, Stichpunkte oder eine Aufgabenliste erzeugen.
- Als Nutzer möchte ich ein Transkript in eine gewählte Sprache übersetzen lassen.

## 3. API & Backend Requirements
- **Enum `PostProcessingMode`:** `Cleanup`, `Summary`, `BulletPoints`, `ActionItems`, `Translate`.
- **Entität `TranscriptVariant`:** `Id`, `AudioJobId` (FK, Cascade), `Mode`, `TargetLanguage?`, `Text`, `Status` (Pending/Completed/Failed), `CreatedAtUtc`. Pro (`Job`, `Mode`, `TargetLanguage`) gibt es genau eine Variante; ein erneuter Lauf überschreibt sie.
- **Endpoints:**
  - `POST /api/audio-jobs/{id}/variants` `{ mode, targetLanguage? }` → `202`; `409`, wenn der Job nicht `Completed` ist; `503`, wenn Ollama deaktiviert ist.
  - `GET /api/audio-jobs/{id}/variants`.
- **SignalR:** `VariantCompleted { jobId, variantId, mode }`.
- **Hinweis:** Das bisherige `ProcessedTranscript` aus S04 entspricht der Variante `Cleanup`. In T2 wird entschieden, ob S04 in dieses Modell migriert wird.

## 4. KI/AI Requirements
- Ein Prompt pro Modus als eingebettete Ressource oder Konfiguration, nicht inline im Code. System-Message: Nur das Ergebnis ausgeben, nichts erfinden. Sprache der Ausgabe ist die Transkriptsprache (außer bei `Translate`).
- Lange Transkripte: Chunking mit Map-Reduce für `Summary` und `ActionItems`, wenn das Kontextfenster überschritten wird.
- Resilienz: Retry-Policy (Polly bzw. `Microsoft.Extensions.Http.Resilience`) laut CLAUDE.md.

## 5. Tasks

### S10-T1 Prompt-Katalog und `IPostProcessingStrategy` · ai · S
- **AC (TDD):**
  - [ ] Test pro Modus: Der Prompt enthält das Transkript und die Modus-Anweisung.
  - [ ] Test für `Translate`: Die Zielsprache ist im Prompt enthalten.

### S10-T2 Entität `TranscriptVariant` und Migration; Entscheidung zu S04 · backend · S
- **AC:**
  - [ ] Migration ist vorhanden.
  - [ ] Die Entscheidung „`ProcessedTranscript` bleibt oder wird Variante“ ist in der Story dokumentiert.

### S10-T3 Varianten-Queue und Verarbeitung · backend · M
- Eigener Channel oder Worker, damit Varianten Whisper-Jobs nicht blockieren. Die Wiederherstellung (S02) gilt analog für offene Varianten.
- **AC:**
  - [ ] Test: Ein Fehler von Ollama setzt die Variante auf `Failed`, der Job bleibt unverändert.

### S10-T4 Chunking für lange Transkripte · ai · M
- **AC:**
  - [ ] Test: Ein Transkript über dem Token-Limit wird in Chunks verarbeitet und zusammengeführt.

### S10-T5 Endpoints · backend · S
- **AC:**
  - [ ] Integrationstests für `202`, `409` und `503`.
  - [ ] Swagger ist aktuell.

### S10-T6 Frontend: Modus-Auswahl und Ergebnis-Tabs · frontend · M
- Die Aktionsleiste im Detail zeigt Modus-Buttons (bei `Translate` mit Sprachauswahl). Die Ergebnisse erscheinen als Tabs neben „Original“ und „Bearbeitet“, während der Verarbeitung mit Lade-Indikator (`aria-busy`). Die Buttons sind ausgeblendet, wenn Ollama nicht verfügbar ist.
- **AC:**
  - [ ] Unit-Tests für Zustände und Event-Handling.

## 6. Acceptance Criteria (DoD)
- [ ] Alle fünf Modi liefern bei einer Meeting-Testaufnahme plausible Ergebnisse (manuell geprüft, Beispiele in der PR-Beschreibung).
- [ ] Der Rohtext bleibt in allen Fällen unverändert.
