# Story S08: Whisper-Modell und Sprache wählbar

> Epic: [E-01](./00-epic.openspec.md) · GitHub: #11 · Phase 2 · Agenten: backend-agent, frontend-agent

## 1. Description
`WhisperTranscriptionService` verwendet fest `GgmlType.Base` und `.WithLanguageDetection()`. Bei deutschen Aufnahmen verbessert ein größeres Modell oder eine vorgegebene Sprache die Qualität deutlich.

## 2. User Stories
- Als Betreiber möchte ich das Standardmodell per Konfiguration festlegen, damit ich Qualität und Geschwindigkeit an meine Hardware anpassen kann.
- Als Nutzer möchte ich beim Upload die Sprache vorgeben, damit die Erkennung bei deutschen Aufnahmen zuverlässiger ist.
- Als Nutzer möchte ich pro Upload ein Modell aus einer freigegebenen Liste wählen.

## 3. API & Backend Requirements
- **Config `Whisper`:** `DefaultModel` (z. B. `Base`), `AllowedModels` (z. B. `[Tiny, Base, Small, Medium, LargeV3]`), `ModelsDirectory`.
- **Upload:** `POST /api/audio-jobs` akzeptiert optionale Form-Felder `language` (ISO-639-1 oder `auto`) und `model`.
- **Modell `AudioJob`:** `RequestedLanguage` (string?), `Model` (string). `Language` bleibt die erkannte oder genutzte Sprache.
- **Neu:** `GET /api/transcription-options` liefert die erlaubten Modelle und Sprachen für das Frontend.

## 4. Tasks

### S08-T1 `WhisperOptions` und Konfiguration · backend · S
- Options-Klasse mit Validierung (`ValidateOnStart`), `appsettings.json`-Abschnitt, Registrierung.
- **AC:**
  - [ ] Test: Ungültiges `DefaultModel` führt beim Start zu einem Fehler.

### S08-T2 Factory-Cache pro Modell · backend · M
- `WhisperTranscriptionService` hält einen `WhisperFactory` pro `GgmlType` (lazy, thread-safe, Download bei Erstnutzung). Der Service wird Singleton statt Scoped, sonst wird der Cache pro Job neu aufgebaut. Das Dispose-Verhalten ist dabei zu prüfen.
- **AC (TDD):**
  - [ ] Test: Zwei Jobs mit demselben Modell teilen sich eine Factory.
  - [ ] Test: Zwei verschiedene Modelle erzeugen zwei Factories.

### S08-T3 Sprache vorgeben · backend · S
- `TranscribeAsync(path, TranscriptionSettings settings, ct)`. Bei gesetzter Sprache wird `.WithLanguage(code)` statt `.WithLanguageDetection()` verwendet.
- **AC:**
  - [ ] Test: Mit `language=de` setzt der Builder die Sprache (über eine Abstraktion oder ein Fake testbar).

### S08-T4 Upload-Parameter, Persistenz und Migration · backend · S
- Validierung gegen die Allowlists, `400` bei unbekanntem Modell oder unbekannter Sprache. Werte am Job speichern (neue Migration), Worker und Wiederherstellung (S02) nutzen sie.
- **AC:**
  - [ ] Integrationstests für gültige und ungültige Werte.

### S08-T5 `GET /api/transcription-options` · backend · XS
- **AC:**
  - [ ] Integrationstest und Swagger.

### S08-T6 Frontend: Auswahl im Upload · frontend · S
- Zwei DaisyUI-`select`-Felder mit `<label for>`, Standardwerte „Automatisch“ bzw. Server-Default. Die Optionen kommen vom Endpoint aus T5, die Sprachnamen aus i18n. Die Detailansicht zeigt Modell und Sprache an.
- **AC:**
  - [ ] Unit-Test: Die Form-Daten enthalten die gewählten Werte.

## 5. Acceptance Criteria (DoD)
- [ ] Eine deutsche Testaufnahme mit `language=de` und `Small` wird korrekt transkribiert (manueller Vergleich mit `Base`/`auto` dokumentiert).
- [ ] README dokumentiert Modelle, Speicherbedarf und Download-Größe.
