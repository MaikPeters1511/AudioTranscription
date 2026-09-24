# Story S11: Sprechererkennung („Wer spricht wann?“)

> Epic: [E-01](./00-epic.openspec.md) · Phase 3 · Agenten: architect-agent, ai-agent, backend-agent, frontend-agent · Voraussetzung: S06 (Segmente)

## 1. Description
Bei Meetings und Interviews ist entscheidend, wer etwas gesagt hat. Sprecher-Diarisierung ordnet Zeitabschnitte anonymen Sprechern zu („Sprecher 1“, „Sprecher 2“), die Nutzer anschließend umbenennen können. Die Verarbeitung muss vollständig lokal und offline laufen, z. B. mit ONNX-Modellen (pyannote-Segmentierung plus Speaker-Embeddings, etwa über sherpa-onnx).

## 2. User Stories
- Als Nutzer möchte ich im Transkript sehen, welcher Sprecher welchen Abschnitt gesagt hat.
- Als Nutzer möchte ich Sprecher umbenennen („Sprecher 1“ → „Anna“), damit Transkript und Export lesbar sind.

## 3. Tasks

### S11-T1 Spike und ADR: Diarisierungs-Ansatz · architect + ai · M
- Kandidaten vergleichen: sherpa-onnx (.NET-Bindings), pyannote als ONNX-Export mit eigenem Clustering, oder ein Python-Sidecar-Container. Kriterien: Offline-Fähigkeit, Lizenz der Modelle, Genauigkeit (DER an 2–3 Testaufnahmen), Laufzeit, Integrationsaufwand in .NET.
- **AC:**
  - [ ] ADR mit Entscheidung und Messwerten.
  - [ ] Folge-Tasks (T2–T5) werden anhand der ADR geschärft.

### S11-T2 `IDiarizationService` und Implementierung · ai · L (nach Spike ggf. splitten)
- Ergebnis: Liste von (`startMs`, `endMs`, `speakerIndex`).
- **AC:**
  - [ ] Test mit einer synthetischen Zwei-Sprecher-Aufnahme erkennt 2 Sprecher.

### S11-T3 Segmente mit Sprechern verknüpfen · backend · S
- `TranscriptSegment.SpeakerIndex` (int?) wird über die größte zeitliche Überlappung zugeordnet. Die Entität `JobSpeaker` (`Index`, `DisplayName`) kommt per Migration hinzu. Die Diarisierung ist optional (Config-Flag und Parameter pro Upload).
- **AC (TDD):**
  - [ ] Unit-Tests der Überlappungslogik inkl. Randfällen.

### S11-T4 Endpoint zum Umbenennen und Export mit Sprechern · backend · S
- `PUT /api/audio-jobs/{id}/speakers/{index}` `{ displayName }`. SRT/VTT (S06) und Textexport stellen den Sprechernamen voran (VTT: `<v Anna>`).
- **AC:**
  - [ ] Integrationstests.
  - [ ] Formatter-Tests aus S06-T4 sind um Sprecher erweitert.

### S11-T5 Frontend: Sprecher-Anzeige und Umbenennen · frontend · M
- Farbmarkierung pro Sprecher (nicht nur Farbe, sondern zusätzlich Name bzw. Label, WCAG 1.4.1). Inline-Umbenennen per Button und Eingabefeld.
- **AC:**
  - [ ] Unit-Tests.
  - [ ] UX-Review (`ux-agent`).

## 4. Acceptance Criteria (DoD)
- [ ] Eine Interview-Testaufnahme mit zwei Sprechern wird zu mindestens 85 % der Sprechzeit korrekt zugeordnet (Messmethode laut ADR).
