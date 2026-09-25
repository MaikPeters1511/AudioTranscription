# Story S03: Temp-Dateien auch nach Fehlern löschen

> Epic: [E-01](./00-epic.openspec.md) · GitHub: #7 · Phase 1 · Agent: backend-agent

## 1. Description
In `TranscriptionWorker.ProcessJobAsync` wird die hochgeladene Datei nur im Erfolgszweig gelöscht. Schlägt die Transkription fehl (z. B. ffmpeg fehlt, Datei defekt), bleibt die Audiodatei dauerhaft in `temp-uploads/` liegen. Das ist ein Datenschutz- und Speicherproblem. Zusätzlich enthält `WhisperTranscriptionService` beim Löschen der Zwischen-WAV ein leeres `catch`, das laut CLAUDE.md untersagt ist.

## 2. User Stories
- Als Nutzer möchte ich, dass meine Aufnahme nach der Verarbeitung gelöscht wird, auch wenn sie fehlschlägt, damit keine privaten Daten liegen bleiben.

## 3. Tasks

### S03-T1 Löschlogik in `finally` bzw. eigenen Service verschieben · backend · S
- Aufräumen in einen `ITempFileStore` (o. ä.) kapseln und im `finally` von `ProcessJobAsync` aufrufen, gesteuert über `UploadOptions.DeleteAfterTranscription`.
- Hinweis: Wenn später Retry (S09) oder Player (S06) die Datei behalten sollen, greift die Aufbewahrungsregel aus D2. Die Löschentscheidung liegt deshalb zentral an einer Stelle.
- **AC (TDD, zuerst rot):**
  - [x] Test: Transkription wirft eine Exception, danach existiert die Datei nicht mehr.
  - [x] Test: Mit `DeleteAfterTranscription=false` bleibt die Datei in beiden Fällen erhalten.
  - [x] Test: Ein Fehler beim Löschen wird als Warning geloggt und ändert den Job-Status nicht.

### S03-T2 Leeres `catch` in `WhisperTranscriptionService` ersetzen · backend · XS
- Beim Löschen der `_16khz.wav` wird die Exception als Warning geloggt, statt sie still zu verschlucken.
- **AC:**
  - [x] Kein leerer `catch`-Block mehr in `AudioTranscription.Infrastructure`.

### S03-T3 Verwaiste Dateien beim Start aufräumen · backend · S
- Beim Start werden Dateien in `temp-uploads/` gelöscht, zu denen kein offener Job existiert und die älter als N Stunden sind (konfigurierbar). Das hängt mit S02 zusammen und sollte nach S02-T2 umgesetzt werden.
- **AC:**
  - [x] Test: Eine verwaiste Datei wird gelöscht, die Datei eines `Pending`-Jobs bleibt erhalten.

## 4. Umsetzungsnotizen (2026-09-24)
- `ITempFileStore`/`TempFileStore` (`AudioTranscription.Api/Storage/`) ist die zentrale Stelle für das Speicherverzeichnis und die Löschentscheidung (`DeleteAfterTranscription`). Auch der Upload-Endpoint bezieht sein Verzeichnis darüber.
- Der Worker räumt im `finally` auf, auch wenn der Job in der DB nicht gefunden wird. **Ausnahme Shutdown:** Wird die Verarbeitung durch das Stoppen des Hosts abgebrochen, bleibt die Datei erhalten, damit S02 den Job nach dem Neustart wieder aufnehmen kann.
- **T3 vorgezogen:** Die Job-ID ergibt sich aus dem Dateinamen (`{jobId}{ext}`), S02-T1 ist dafür nicht nötig. `OrphanedUploadCleanupService` läuft einmal beim Start. Es löscht Dateien, die älter als `Upload:OrphanedFileRetentionHours` (Standard 24) sind und zu keinem offenen Job gehören. Bei `DeleteAfterTranscription=false` bleiben die Dateien aller bekannten Jobs erhalten.

> **Hinweis (2026-09-24):** Mit D2 (S09) wird die Upload-Datei nach einem **Fehler** nicht mehr gelöscht, sondern für einen Neustart behalten. Siehe S09, Abschnitt 5.

## 5. Acceptance Criteria (DoD)
- [ ] Nach einem fehlgeschlagenen Job ist `temp-uploads/` leer (bei Standard-Konfiguration).
