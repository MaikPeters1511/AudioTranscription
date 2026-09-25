# Story S12: Direkt im Browser aufnehmen

> Epic: [E-01](./00-epic.openspec.md) · GitHub: #16 · Phase 3 · Agenten: frontend-agent, backend-agent, ux-agent

## 1. Description
Nutzer sollen per Mikrofon direkt im Browser aufnehmen (MediaRecorder-API), ohne vorher eine Datei zu speichern. Die Aufnahme wird wie ein normaler Upload verarbeitet.

## 2. User Stories
- Als Nutzer möchte ich im Browser auf „Aufnehmen“ klicken, sprechen, stoppen und die Aufnahme direkt transkribieren lassen.

## 3. Tasks

### S12-T1 Backend: `audio/webm` zulassen · backend · XS
- Chrome und Firefox liefern `audio/webm;codecs=opus`, Safari `audio/mp4`. Der Content-Type wird ohne Parameter verglichen, Magic Bytes EBML (`1A 45 DF A3`).
- **AC (TDD):**
  - [x] Magic-Bytes-Test (`MagicBytesValidatorTests`: EBML-Header wird akzeptiert, andere Bytes abgelehnt).
  - [x] Integrationstest: `audio/webm;codecs=opus` wird akzeptiert (`UploadStorageIntegrationTests.Upload_WithWebmCodecsParameter_IsAccepted`; gespeicherter `ContentType` ist ohne Parameter `audio/webm`).

### S12-T2 `AudioRecorderService` · frontend · S
- Kapselt `getUserMedia` und `MediaRecorder`, wählt das Format per `MediaRecorder.isTypeSupported`. Zustände als Signals: `idle`, `recording`, `stopped`, `error`. Behandelt verweigerte Berechtigung.
- **AC:**
  - [x] Unit-Tests mit gemocktem `MediaRecorder`, auch für den Fall „Berechtigung verweigert“ (`audio-recorder.service.spec.ts`, 9 Tests: Formatwahl inkl. Fallback, `permission-denied`, `unsupported` ohne `MediaRecorder`, `recording-failed`, Stop räumt die Mikrofon-Tracks auf, Reset).

### S12-T3 Recorder-Komponente · frontend · M
- Start/Stopp-Button (`aria-pressed`), Laufzeitanzeige, optional ein Pegel-Meter (`AnalyserNode`), Vorhören per `<audio>`, danach „Transkribieren“ oder „Verwerfen“. Die Auswahl aus S08 wird mitgesendet, falls vorhanden. Ein Maximaldauer-Hinweis orientiert sich am Upload-Limit.
- **AC:**
  - [x] Vollständig per Tastatur bedienbar (native `<button>`-Elemente, keine Custom-Div-Handler).
  - [x] Statuswechsel werden per `aria-live` angesagt (`aria-live="polite"`-Region, separat von der laufenden Zeitanzeige, damit Screenreader nicht sekündlich unterbrochen werden).
  - [x] Alle Texte kommen aus i18n (`recorder.*`-Namespace in `en.json`/`de.json`).
- Abweichung: Das optionale Pegel-Meter (`AnalyserNode`) wurde **nicht** umgesetzt — es ist laut Spec optional, in Web Audio API nicht sinnvoll unit-testbar (kein `AnalyserNode` in jsdom) und für den Kernnutzen (aufnehmen → transkribieren) nicht nötig. Stattdessen zeigt die Komponente einen laufenden Timer und einen farbwechselnden Aufnahme-Button als Feedback.

### S12-T4 UX-Review und Browser-Test · ux + qa · S
- **AC:**
  - [x] Manueller Test in Chrome, Firefox und Safari ist dokumentiert. **Eingeschränkt:** Diese Sandbox hat nur Chromium installiert (kein Firefox/Safari-Binary verfügbar). Der volle Ablauf (Aufnehmen → Stoppen → Vorhören → Transkribieren → Upload) wurde end-to-end in Chromium mit einem echten `MediaRecorder`/`getUserMedia` (Fake-Mikrofon-Gerät) verifiziert, inklusive erfolgreichem `POST /api/audio-jobs` mit `audio/webm`. Ein Test in echtem Firefox und Safari steht noch aus und sollte vor einem produktiven Release nachgeholt werden.
  - [x] Hinweis auf HTTPS-Pflicht für `getUserMedia` steht im README (Abschnitt „Direkt im Browser aufnehmen“).

## 4. Acceptance Criteria (DoD)
- [~] Eine 30-sekündige Browser-Aufnahme wird erfolgreich transkribiert. Der Upload- und Format-Pfad ist end-to-end verifiziert (Chromium, Fake-Mikrofon); eine echte 30-Sekunden-Transkription mit Whisper wurde hier nicht durchgeführt, da das Whisper-Modell aus dieser Sandbox nicht heruntergeladen werden kann (siehe S08-Einschränkung).

## 5. Umsetzungsnotizen

- **Backend (T1):** Content-Type wird jetzt ohne Parameter verglichen (`Split(';')[0].Trim()`), da Browser `audio/webm;codecs=opus` senden. `audio/webm` ist in `UploadOptions.AllowedContentTypes` und `appsettings.json` ergänzt, `MagicBytesValidator` prüft den EBML-Header (`1A 45 DF A3`).
- **Frontend (T2):** `AudioRecorderService` wählt das Aufnahmeformat über `MediaRecorder.isTypeSupported` in der Reihenfolge `audio/webm;codecs=opus` → `audio/webm` → `audio/mp4` (Safari-Fallback). Verweigerte Mikrofonberechtigung, ein fehlendes `MediaRecorder` (nicht unterstützter Browser) und ein Laufzeitfehler des Recorders liefern je einen eigenen `errorKind`, damit die Komponente eine passende Fehlermeldung zeigen kann, statt zu werfen.
- **Frontend (T3):** `RecorderComponent` ist in die Upload-Seite als zweiter Reiter integriert („Datei hochladen“ / „Aufnehmen“); eine fertige Aufnahme wird als `File` genau wie ein ausgewähltes Datei-Objekt an den bestehenden Upload-Pfad übergeben (inkl. Modell-/Sprach-/Diarisierungs-Auswahl). `webm` wurde dafür zur clientseitigen Dateityp-/Endungsprüfung ergänzt.
- **Bekannte Lücke:** Firefox/Safari-Test steht aus (siehe T4); das optionale Pegel-Meter wurde bewusst nicht gebaut (siehe T3).
