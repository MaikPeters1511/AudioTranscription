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
  - [x] Test pro Modus: Der Prompt enthält das Transkript und die Modus-Anweisung.
  - [x] Test für `Translate`: Die Zielsprache ist im Prompt enthalten.

### S10-T2 Entität `TranscriptVariant` und Migration; Entscheidung zu S04 · backend · S
- **AC:**
  - [x] Migration ist vorhanden.
  - [x] Die Entscheidung „`ProcessedTranscript` bleibt oder wird Variante“ ist in der Story dokumentiert: siehe Umsetzungsnotizen (migriert zur Variante `Cleanup`).

### S10-T3 Varianten-Queue und Verarbeitung · backend · M
- Eigener Channel oder Worker, damit Varianten Whisper-Jobs nicht blockieren. Die Wiederherstellung (S02) gilt analog für offene Varianten.
- **AC:**
  - [x] Test: Ein Fehler von Ollama setzt die Variante auf `Failed`, der Job bleibt unverändert.

### S10-T4 Chunking für lange Transkripte · ai · M
- **AC:**
  - [x] Test: Ein Transkript über dem Token-Limit wird in Chunks verarbeitet und zusammengeführt.

### S10-T5 Endpoints · backend · S
- **AC:**
  - [x] Integrationstests für `202`, `409` und `503`.
  - [x] Swagger: Die API hat noch kein OpenAPI-Dokument. Die Endpoints tragen wie die übrigen `WithName`/`WithDescription`.

### S10-T6 Frontend: Modus-Auswahl und Ergebnis-Tabs · frontend · M
- Die Aktionsleiste im Detail zeigt Modus-Buttons (bei `Translate` mit Sprachauswahl). Die Ergebnisse erscheinen als Tabs neben „Original“ und „Bearbeitet“, während der Verarbeitung mit Lade-Indikator (`aria-busy`). Die Buttons sind ausgeblendet, wenn Ollama nicht verfügbar ist.
- **AC:**
  - [x] Unit-Tests für Zustände und Event-Handling.

## 6. Umsetzungsnotizen (2026-09-24)
- **Entscheidung T2 (ProcessedTranscript vs. Variante):** `AudioJob.ProcessedTranscript` entfällt. Eine Migration überführt vorhandene Werte in eine `TranscriptVariant` mit `Mode=Cleanup`, `Status=Completed`. Die automatische Nachbearbeitung aus S04 bleibt erhalten: Der Worker legt nach jedem erfolgreich transkribierten Job automatisch eine `Cleanup`-Variante an und reiht sie in die neue Varianten-Queue ein, sofern Ollama konfiguriert ist. Nutzer können sie über denselben Button wie die übrigen Modi jederzeit neu erzeugen.
- **Prompt-Katalog (T1):** `PostProcessingPromptCatalog` lädt Vorlagen als eingebettete Ressourcen unter `Prompts/*.txt` (eine Datei je Modus, plus `-chunk`/`-reduce`-Varianten für Summary/ActionItems). Jede Vorlage weist das Modell an, nur das Ergebnis auszugeben, nichts zu erfinden und die Sprache des Transkripts beizubehalten (außer bei `Translate`).
- **Abweichung, Zielsprache im Prompt:** Es wird der ISO-Code (z. B. „fr“) eingesetzt, kein ausgeschriebener Sprachname. Sprachmodelle verstehen gängige ISO-Codes zuverlässig; eine Namensauflösung gäbe es serverseitig ohnehin nicht ohne zusätzliche Abhängigkeit.
- **Entität und Migration (T2):** `TranscriptVariant` mit `Mode`, `TargetLanguage?`, `Status`, `Text?`, `ErrorMessage?`. Kein DB-Unique-Index auf (`AudioJobId`, `Mode`, `TargetLanguage`): SQL Server behandelt jeden NULL-Wert in einem Unique-Index als eigenständig, das würde bei nicht-`Translate`-Modi (immer `TargetLanguage = NULL`) mehrere Zeilen zulassen. „Ein erneuter Lauf überschreibt“ wird deshalb im Endpoint durchgesetzt (Suchen, dann Aktualisieren oder Anlegen), nicht per Datenbank-Constraint.
- **Queue und Worker (T3):** Eigener `VariantQueue`/`VariantWorker`, unabhängig vom Whisper-Worker, damit ein LLM-Aufruf nie einen Transkriptions-Job blockiert oder umgekehrt. `VariantRecoveryService` reiht beim Start alle noch `Pending` gebliebenen Varianten erneut ein (analog `JobRecoveryService` aus S02). Schlägt die Erzeugung fehl, wird nur die Variante auf `Failed` gesetzt; der Job selbst bleibt unangetastet.
- **Retry (S09) räumt auf:** Beim erneuten Einreihen eines fehlgeschlagenen oder abgebrochenen Jobs werden vorhandene Varianten dieses Jobs gelöscht, da sie sich auf den alten (verworfenen) Rohtext beziehen.
- **Resilienz (T4):** `OllamaVariantGenerator` wiederholt einen fehlgeschlagenen Modell-Aufruf über Polly (`Polly.Core`) zweimal mit kurzer exponentieller Wartezeit. Eine eigene Abbruchanforderung des Aufrufers wird nie wiederholt, das prüft Polly anhand des übergebenen `CancellationToken` unabhängig von der Retry-Bedingung.
- **Chunking (T4):** `TranscriptChunker` ist eine reine, ohne Modell testbare Funktion. Sie zerlegt den Text wortweise in Abschnitte bis zu `PostProcessing:MaxChunkLength` Zeichen, ohne ein Wort zu zerschneiden. Nur `Summary` und `ActionItems` nutzen Map-Reduce (Vorgabe der Story); bei genau einem Abschnitt entfällt der Reduce-Schritt und es wird direkt der volle-Transkript-Prompt verwendet.
- **Abweichung, Zeichen statt Token:** Ohne Tokenizer für das konfigurierte Ollama-Modell dient die Zeichenzahl als Näherung. Das ist dokumentiert und über `PostProcessing:MaxChunkLength` einstellbar.
- **Endpoints (T5):** `POST/GET /api/audio-jobs/{id}/variants`. `400` bei unbekanntem Modus oder fehlender/nicht unterstützter Zielsprache (dieselbe Sprachliste wie `Whisper:SupportedLanguages`, siehe Abweichung unten), `404` bei unbekanntem Job, `409` außer bei `Completed`, `503` ohne konfiguriertes Ollama.
- **Abweichung, gemeinsame Sprachliste:** Die Zielsprachen für `Translate` sind dieselben wie `Whisper:SupportedLanguages`, es gibt keine eigene Konfiguration. Das vermeidet zwei parallel zu pflegende Sprachlisten; im Frontend ist damit auch die Sprachauswahl der Detailansicht sofort konsistent mit der Upload-Seite (S08).
- **Frontend (T6):**
  - Aktionsleiste „Weitere Fassungen“ in der Detailansicht, nur sichtbar, wenn `GET /api/transcription-options` `postProcessingEnabled: true` meldet (neues Feld an diesem bestehenden S08-Endpoint statt eines eigenen).
  - Ein Knopf je Modus (außer `Translate`, das eine eigene Sprachauswahl plus Knopf hat). Während eine Variante `Pending` ist, zeigt ihr Knopf `aria-busy="true"`, ist deaktiviert und trägt einen Lade-Spinner.
  - Ergebnisse erscheinen als zusätzliche Tabs neben „Original“ (bestehendes Tab-Muster aus S04, jetzt für beliebig viele Varianten statt nur „Original“/„Bearbeitet“).
  - **Abweichung, Standardauswahl:** Die Tabs starten immer auf „Original“, nicht mehr automatisch auf der bearbeiteten Fassung. Da Varianten jetzt mehrere und optionale Ergebnisse sein können statt eines einzelnen automatischen Felds, ist ein fester, vorhersagbarer Standard klarer als zu erraten, welche Variante gemeint ist.
  - Fehlgeschlagene Varianten erscheinen als Liste mit Fehlermeldung unterhalb der Aktionsleiste; ein erneuter Klick auf denselben Modus versucht es erneut.
  - `AudioJobService.variants` wird beim Öffnen eines Jobs geladen und bei `VariantCompleted` (SignalR) für den aktuell geöffneten Job neu geladen.
- **Offen (DoD):** Der manuelle Vergleich aller fünf Modi an einer echten Aufnahme braucht ein laufendes Ollama-Modell, dessen Download in dieser Entwicklungsumgebung gesperrt ist.

## 7. Acceptance Criteria (DoD)
- [ ] Alle fünf Modi liefern bei einer Meeting-Testaufnahme plausible Ergebnisse (manuell geprüft, Beispiele in der PR-Beschreibung). **Offen:** braucht ein laufendes Ollama-Modell; in dieser Umgebung nicht verfügbar.
- [x] Der Rohtext bleibt in allen Fällen unverändert.
