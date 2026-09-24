# Story S11: Sprechererkennung („Wer spricht wann?“)

> Epic: [E-01](./00-epic.openspec.md) · GitHub: #19 · Phase 3 · Agenten: architect-agent, ai-agent, backend-agent, frontend-agent · Voraussetzung: S06 (Segmente)

## 1. Description
Bei Meetings und Interviews ist entscheidend, wer etwas gesagt hat. Sprecher-Diarisierung ordnet Zeitabschnitte anonymen Sprechern zu („Sprecher 1“, „Sprecher 2“), die Nutzer anschließend umbenennen können. Die Verarbeitung muss vollständig lokal und offline laufen, z. B. mit ONNX-Modellen (pyannote-Segmentierung plus Speaker-Embeddings, etwa über sherpa-onnx).

## 2. User Stories
- Als Nutzer möchte ich im Transkript sehen, welcher Sprecher welchen Abschnitt gesagt hat.
- Als Nutzer möchte ich Sprecher umbenennen („Sprecher 1“ → „Anna“), damit Transkript und Export lesbar sind.

## 3. Tasks

### S11-T1 Spike und ADR: Diarisierungs-Ansatz · architect + ai · M
- Kandidaten vergleichen: sherpa-onnx (.NET-Bindings), pyannote als ONNX-Export mit eigenem Clustering, oder ein Python-Sidecar-Container. Kriterien: Offline-Fähigkeit, Lizenz der Modelle, Genauigkeit (DER an 2–3 Testaufnahmen), Laufzeit, Integrationsaufwand in .NET.
- **AC:**
  - [x] ADR mit Entscheidung und Messwerten: [ADR 0004](../../docs/adr/0004-sprechererkennung.md). Entscheidung: sherpa-onnx (`org.k2fsa.sherpa.onnx`). Messwerte zur Genauigkeit (DER) konnten nicht erhoben werden, siehe ADR-Konsequenzen (huggingface.co/github.com sind aus dieser Umgebung gesperrt).
  - [x] Folge-Tasks (T2–T5) sind anhand der ADR geschärft: `IDiarizationService` kapselt sherpa-onnx, `OfflineSpeakerDiarizationSegment` liefert (Start, End, Speaker) in Sekunden, die WAV-Konvertierung aus S08 wird wiederverwendet.

### S11-T2 `IDiarizationService` und Implementierung · ai · L (nach Spike ggf. splitten)
- Ergebnis: Liste von (`startMs`, `endMs`, `speakerIndex`).
- **AC:**
  - [~] Test mit einer synthetischen Zwei-Sprecher-Aufnahme erkennt 2 Sprecher. `SherpaOnnxDiarizationService` ist implementiert und über Reflection gegen die reale sherpa-onnx-API (1.13.8) verifiziert; ein Testlauf mit echten Modellen war in dieser Sandbox nicht möglich (huggingface.co/github.com liefern 403, siehe ADR 0004). Getestet wurde stattdessen die reine Zuordnungslogik (T3) sowie `DiarizationOptionsValidator` und die Worker-Integration mit einem gemockten `IDiarizationService`.

### S11-T3 Segmente mit Sprechern verknüpfen · backend · S
- `TranscriptSegment.SpeakerIndex` (int?) wird über die größte zeitliche Überlappung zugeordnet. Die Entität `JobSpeaker` (`Index`, `DisplayName`) kommt per Migration hinzu. Die Diarisierung ist optional (Config-Flag und Parameter pro Upload).
- **AC (TDD):**
  - [x] Unit-Tests der Überlappungslogik inkl. Randfällen (`SpeakerOverlapAssignerTests`: keine Überlappung, volle Enthaltung, Vergleich zweier Intervalle, exaktes Unentschieden zugunsten des niedrigeren Index, Lücke, Berührung an der Grenze, Nulldauer, vertauschte/negative Eingabe).

### S11-T4 Endpoint zum Umbenennen und Export mit Sprechern · backend · S
- `PUT /api/audio-jobs/{id}/speakers/{index}` `{ displayName }`. SRT/VTT (S06) und Textexport stellen den Sprechernamen voran (VTT: `<v Anna>`).
- **AC:**
  - [x] Integrationstests (`DiarizationIntegrationTests`: Upload-Flag, Segmente/Untertitel mit aufgelösten Namen, GET/PUT `/speakers`, Validierungsfehler).
  - [x] Formatter-Tests aus S06-T4 sind um Sprecher erweitert (`SubtitleFormatterTests`: SRT-Präfix, VTT `<v Name>`, kein Präfix ohne Zuordnung).

### S11-T5 Frontend: Sprecher-Anzeige und Umbenennen · frontend · M
- Farbmarkierung pro Sprecher (nicht nur Farbe, sondern zusätzlich Name bzw. Label, WCAG 1.4.1). Inline-Umbenennen per Button und Eingabefeld.
- **AC:**
  - [x] Unit-Tests (`transcript-player.component.spec.ts`: Sprecher-Badges pro Segment, Toolbar mit Umbenennen-Buttons, Umbenennen aktualisiert Toolbar+Segmente; `upload.component.spec.ts`/`audio-job.service.spec.ts`: `diarize`-Checkbox nur bei `diarizationEnabled`, Upload-Flag).
  - [x] UX-Review (inline, siehe Umsetzungsnotizen).

## 4. Acceptance Criteria (DoD)
- [ ] Eine Interview-Testaufnahme mit zwei Sprechern wird zu mindestens 85 % der Sprechzeit korrekt zugeordnet (Messmethode laut ADR). **Offen:** in dieser Sandbox nicht überprüfbar (siehe Umsetzungsnotizen).

## 5. Umsetzungsnotizen

- **Backend:** `SherpaOnnxDiarizationService` kapselt `SherpaOnnx.OfflineSpeakerDiarization` (Paket `org.k2fsa.sherpa.onnx` 1.13.8); Modelle werden lazy geladen und für die Prozesslaufzeit gecacht, Aufrufe sind über ein `SemaphoreSlim` serialisiert. Die WAV-Konvertierung wurde aus `WhisperTranscriptionService` in `Audio16kHzWavConverter` extrahiert und wird von beiden Diensten genutzt.
- **Zuordnung:** `SpeakerOverlapAssigner` ordnet jedes `TranscriptSegment` dem Sprecher mit der größten zeitlichen Überlappung zu (Unentschieden → niedrigerer Index gewinnt). Reine Domain-Logik, vollständig unit-getestet inkl. Randfälle (Lücke, Berührung, Nulldauer, vertauschte Eingabe).
- **Konfiguration:** neue `Diarization`-Sektion in `appsettings.json` (`Enabled`, `SegmentationModelPath`, `EmbeddingModelPath`, `Threshold`, `NumThreads`), standardmäßig deaktiviert. `GET /api/transcription-options` liefert zusätzlich `diarizationEnabled`, damit das Frontend die Diarisierungs-Option nur anzeigt, wenn der Server dafür konfiguriert ist (analog zu `postProcessingEnabled` aus S10).
- **Export:** SRT stellt `"Name: "` voran, VTT nutzt `<v Name>Text</v>` (Voice-Span). Ohne Sprecherzuordnung bleibt der Export unverändert (Rückwärtskompatibilität zu S06).
- **Naming:** Unbenannte Sprecher heißen `"Sprecher N"` (1-basiert); `PUT /api/audio-jobs/{id}/speakers/{index}` erlaubt das Umbenennen (Validierung: nicht leer, max. 100 Zeichen).
- **Frontend:** `TranscriptPlayerComponent` zeigt bei einer diarisierten Aufnahme eine Sprecher-Toolbar (farbiges Badge + Name + Umbenennen-Button) sowie ein Sprecher-Badge vor jedem Transkript-Segment. WCAG 1.4.1: Farbe wird nie allein verwendet, der Sprechername steht immer als Text daneben. Umbenennen per Inline-Eingabefeld (Enter zum Speichern, Escape zum Abbrechen), aktualisiert Toolbar und Segmente sofort. Der Upload-Dialog zeigt eine "Sprechererkennung"-Checkbox nur, wenn der Server sie unterstützt (`diarizationEnabled`). Manuell im Browser mit gemockten API-Antworten verifiziert (Playwright: Checkbox-Sichtbarkeit, Badges, Umbenennen-Flow inkl. Live-Update).
- **Bekannte Einschränkung (unverändert seit S08):** huggingface.co und github.com liefern aus dieser Sandbox `403`, sodass die realen pyannote-Segmentierungs- und Embedding-ONNX-Modelle nicht heruntergeladen werden konnten. Die tatsächliche Diarisierungsgenauigkeit (DER) wurde daher **nicht** gemessen; die API-Oberfläche wurde stattdessen per NuGet-Restore + Reflection gegen die reale sherpa-onnx-Bibliothek verifiziert. Vor einem produktiven Einsatz muss ein Betreiber mit Internetzugang die Modelle beschaffen und einen echten DER-Test durchführen.
- **GitGuardian:** weiterhin nur der bekannte Altfund aus Commit `39789d6` (Testpasswort), History-Rewrite laut Entscheidung D3 ausgeschlossen; wartet auf manuelle Aktion im Dashboard.
