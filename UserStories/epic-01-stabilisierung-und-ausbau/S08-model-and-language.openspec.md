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
  - [x] Test: Ungültiges `DefaultModel` führt beim Start zu einem Fehler.

### S08-T2 Factory-Cache pro Modell · backend · M
- `WhisperTranscriptionService` hält einen `WhisperFactory` pro `GgmlType` (lazy, thread-safe, Download bei Erstnutzung). Der Service wird Singleton statt Scoped, sonst wird der Cache pro Job neu aufgebaut. Das Dispose-Verhalten ist dabei zu prüfen.
- **AC (TDD):**
  - [x] Test: Zwei Jobs mit demselben Modell teilen sich eine Factory.
  - [x] Test: Zwei verschiedene Modelle erzeugen zwei Factories.

### S08-T3 Sprache vorgeben · backend · S
- `TranscribeAsync(path, TranscriptionSettings settings, ct)`. Bei gesetzter Sprache wird `.WithLanguage(code)` statt `.WithLanguageDetection()` verwendet.
- **AC:**
  - [x] Test: Mit `language=de` setzt der Builder die Sprache (über eine Abstraktion oder ein Fake testbar). Abweichung siehe Umsetzungsnotizen.

### S08-T4 Upload-Parameter, Persistenz und Migration · backend · S
- Validierung gegen die Allowlists, `400` bei unbekanntem Modell oder unbekannter Sprache. Werte am Job speichern (neue Migration), Worker und Wiederherstellung (S02) nutzen sie.
- **AC:**
  - [x] Integrationstests für gültige und ungültige Werte.

### S08-T5 `GET /api/transcription-options` · backend · XS
- **AC:**
  - [x] Integrationstest. Swagger: Die API hat noch kein OpenAPI-Dokument. Der Endpoint trägt wie die übrigen `WithName` und `WithDescription`.

### S08-T6 Frontend: Auswahl im Upload · frontend · S
- Zwei DaisyUI-`select`-Felder mit `<label for>`, Standardwerte „Automatisch“ bzw. Server-Default. Die Optionen kommen vom Endpoint aus T5, die Sprachnamen aus i18n. Die Detailansicht zeigt Modell und Sprache an.
- **AC:**
  - [x] Unit-Test: Die Form-Daten enthalten die gewählten Werte.

## 5. Umsetzungsnotizen (2026-09-24)
- **Konfiguration (T1):** `WhisperOptions` liest den Abschnitt `Whisper` (`DefaultModel`, `AllowedModels`, `SupportedLanguages`, `ModelsDirectory`). `WhisperOptionsValidator` prüft beim Start (`ValidateOnStart`):
  - Jedes Modell ist ein Name aus Whisper.nets `GgmlType`.
  - `DefaultModel` steht in `AllowedModels`.
  - Sprachen sind ISO-Codes in Kleinbuchstaben. `auto` ist immer erlaubt und steht nicht in der Liste.
- **Listen nur in `appsettings.json`:** Die Klasse belegt die Listen nicht vor. Der Configuration-Binder würde sonst die Werte aus der Konfiguration an die Vorbelegung anhängen, statt sie zu ersetzen.
- **Factory-Cache (T2):** `KeyedAsyncCache<TKey, TValue>` hält pro `GgmlType` genau eine `WhisperFactory`:
  - Sie wird auch bei parallelen Aufrufen nur einmal geladen.
  - Ein fehlgeschlagener Download wird nicht gespeichert, der nächste Aufruf versucht es erneut.
  - Bricht ein Aufrufer ab, lädt der gemeinsame Download weiter.
  - `DisposeAsync` bricht laufende Downloads ab und gibt geladene Modelle frei.
- **Singleton:** `WhisperTranscriptionService` ist jetzt Singleton, der Host gibt ihn beim Beenden asynchron frei.
- **Modell-Download:** Ein Modell wird zuerst als `.download`-Datei geladen und erst danach umbenannt. Nach einem Abbruch bleibt so keine halbe Modelldatei liegen.
- **Sprache (T3):** Die Signatur ist `TranscribeAsync(path, TranscriptionSettings(Model, Language), ct)`. `Language == null` bedeutet `.WithLanguageDetection()`, sonst gilt `.WithLanguage(code)`. Meldet Whisper keine Sprache, wird die vorgegebene als `Language` gespeichert.
- **Nicht mehr freigegebenes Modell:** Der Service lehnt es ab, z. B. bei einem Retry, nachdem der Betreiber die Liste verkleinert hat. Der Job endet dann als `Failed` mit Begründung.
- **AC T3, Abweichung:** `WhisperProcessorBuilder` lässt sich ohne echtes Modell nicht erzeugen, deshalb gibt es keine Abstraktion nur für den Test. Getestet ist die Kette bis zum Aufruf:
  - `TryResolveLanguage`: `auto` und leer ergeben `null`, `DE` ergibt `de`.
  - Ein Worker-Test prüft, dass `Model` und `RequestedLanguage` des Jobs im Service ankommen.
  - Die Verzweigung im Service ist eine einzelne Zeile.
- **Upload (T4):** Die Form-Felder `model` und `language` sind optional, Groß- und Kleinschreibung spielt keine Rolle. Gespeichert wird der kanonische Wert (`small` → `Small`, `DE` → `de`, `auto` → `null`). Bei unbekannten Werten antwortet die API mit `400 ValidationProblem` (Schlüssel ist der Feldname) und legt weder Job noch Datei an.
- **Persistenz (T4):** Die Migration `AddTranscriptionSettings` gibt bestehenden Jobs `Model = "Base"`, das bisher feste Modell. Der Worker liest die Werte am Job. Sie gelten deshalb auch für die Wiederherstellung nach einem Neustart (S02) und für Retry (S09).
- **Options-Endpoint (T5):** `GET /api/transcription-options` liefert `{ models, defaultModel, languages }` und erfordert eine Anmeldung.
- **Frontend (T6):** Zwei `select`-Felder mit `<label for>` stehen über der Drop-Zone. Voreingestellt sind das Server-Standardmodell und „Automatisch erkennen“. Liefert der Endpoint nichts, entfallen die Felder und der Server nimmt seine Standards.
- **Detailansicht:** Sie zeigt das Modell, den Namen der Sprache und den Hinweis „Beim Upload vorgegeben“ bzw. „Automatisch erkannt“.
- **Abweichung, Sprachnamen:** Sie kommen nicht aus den i18n-Dateien, sondern aus `Intl.DisplayNames` in der aktiven UI-Sprache. So hat jeder freigegebene Code einen Namen, ohne dass Übersetzungen nachzupflegen sind. Aus i18n kommen „Automatisch erkennen“, die Labels und der Modell-Hinweis.
- **Offen (DoD):** Der manuelle Qualitätsvergleich braucht die Whisper-Modelle. Ihr Download von huggingface.co ist in der Entwicklungsumgebung gesperrt, der Vergleich steht deshalb noch aus.

## 6. Acceptance Criteria (DoD)
- [ ] Eine deutsche Testaufnahme mit `language=de` und `Small` wird korrekt transkribiert (manueller Vergleich mit `Base`/`auto` dokumentiert).
- [x] README dokumentiert Modelle, Speicherbedarf und Download-Größe.
