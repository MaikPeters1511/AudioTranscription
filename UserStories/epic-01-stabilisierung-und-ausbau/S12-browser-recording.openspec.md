# Story S12: Direkt im Browser aufnehmen

> Epic: [E-01](./00-epic.openspec.md) · Phase 3 · Agenten: frontend-agent, backend-agent, ux-agent

## 1. Description
Nutzer sollen per Mikrofon direkt im Browser aufnehmen (MediaRecorder-API), ohne vorher eine Datei zu speichern. Die Aufnahme wird wie ein normaler Upload verarbeitet.

## 2. User Stories
- Als Nutzer möchte ich im Browser auf „Aufnehmen“ klicken, sprechen, stoppen und die Aufnahme direkt transkribieren lassen.

## 3. Tasks

### S12-T1 Backend: `audio/webm` zulassen · backend · XS
- Chrome und Firefox liefern `audio/webm;codecs=opus`, Safari `audio/mp4`. Der Content-Type wird ohne Parameter verglichen, Magic Bytes EBML (`1A 45 DF A3`).
- **AC (TDD):**
  - [ ] Magic-Bytes-Test.
  - [ ] Integrationstest: `audio/webm;codecs=opus` wird akzeptiert.

### S12-T2 `AudioRecorderService` · frontend · S
- Kapselt `getUserMedia` und `MediaRecorder`, wählt das Format per `MediaRecorder.isTypeSupported`. Zustände als Signals: `idle`, `recording`, `stopped`, `error`. Behandelt verweigerte Berechtigung.
- **AC:**
  - [ ] Unit-Tests mit gemocktem `MediaRecorder`, auch für den Fall „Berechtigung verweigert“.

### S12-T3 Recorder-Komponente · frontend · M
- Start/Stopp-Button (`aria-pressed`), Laufzeitanzeige, optional ein Pegel-Meter (`AnalyserNode`), Vorhören per `<audio>`, danach „Transkribieren“ oder „Verwerfen“. Die Auswahl aus S08 wird mitgesendet, falls vorhanden. Ein Maximaldauer-Hinweis orientiert sich am Upload-Limit.
- **AC:**
  - [ ] Vollständig per Tastatur bedienbar.
  - [ ] Statuswechsel werden per `aria-live` angesagt.
  - [ ] Alle Texte kommen aus i18n.

### S12-T4 UX-Review und Browser-Test · ux + qa · S
- **AC:**
  - [ ] Manueller Test in Chrome, Firefox und Safari ist dokumentiert.
  - [ ] Hinweis auf HTTPS-Pflicht für `getUserMedia` steht im README.

## 4. Acceptance Criteria (DoD)
- [ ] Eine 30-sekündige Browser-Aufnahme wird erfolgreich transkribiert.
