# Story S03: Temp-Dateien auch nach Fehlern löschen

> Epic: [E-01](./00-epic.openspec.md) · Phase 1 · Agent: backend-agent

## 1. Description
In `TranscriptionWorker.ProcessJobAsync` wird die hochgeladene Datei nur im Erfolgszweig gelöscht. Schlägt die Transkription fehl (z. B. ffmpeg fehlt, Datei defekt), bleibt die Audiodatei dauerhaft in `temp-uploads/` liegen. Das ist ein Datenschutz- und Speicherproblem. Zusätzlich enthält `WhisperTranscriptionService` beim Löschen der Zwischen-WAV ein leeres `catch`, das laut CLAUDE.md untersagt ist.

## 2. User Stories
- Als Nutzer möchte ich, dass meine Aufnahme nach der Verarbeitung gelöscht wird, auch wenn sie fehlschlägt, damit keine privaten Daten liegen bleiben.

## 3. Tasks

### S03-T1 Löschlogik in `finally` bzw. eigenen Service verschieben · backend · S
- Aufräumen in einen `ITempFileStore` (o. ä.) kapseln und im `finally` von `ProcessJobAsync` aufrufen, gesteuert über `UploadOptions.DeleteAfterTranscription`.
- Hinweis: Wenn später Retry (S09) oder Player (S06) die Datei behalten sollen, greift die Aufbewahrungsregel aus D2. Die Löschentscheidung liegt deshalb zentral an einer Stelle.
- **AC (TDD, zuerst rot):**
  - [ ] Test: Transkription wirft eine Exception, danach existiert die Datei nicht mehr.
  - [ ] Test: Mit `DeleteAfterTranscription=false` bleibt die Datei in beiden Fällen erhalten.
  - [ ] Test: Ein Fehler beim Löschen wird als Warning geloggt und ändert den Job-Status nicht.

### S03-T2 Leeres `catch` in `WhisperTranscriptionService` ersetzen · backend · XS
- Beim Löschen der `_16khz.wav` wird die Exception als Warning geloggt, statt sie still zu verschlucken.
- **AC:**
  - [ ] Kein leerer `catch`-Block mehr in `AudioTranscription.Infrastructure`.

### S03-T3 Verwaiste Dateien beim Start aufräumen · backend · S
- Beim Start werden Dateien in `temp-uploads/` gelöscht, zu denen kein offener Job existiert und die älter als N Stunden sind (konfigurierbar). Das hängt mit S02 zusammen und sollte nach S02-T2 umgesetzt werden.
- **AC:**
  - [ ] Test: Eine verwaiste Datei wird gelöscht, die Datei eines `Pending`-Jobs bleibt erhalten.

## 4. Acceptance Criteria (DoD)
- [ ] Nach einem fehlgeschlagenen Job ist `temp-uploads/` leer (bei Standard-Konfiguration).
