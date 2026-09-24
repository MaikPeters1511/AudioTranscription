# Story S02: Offene Jobs beim Neustart wiederherstellen

> Epic: [E-01](./00-epic.openspec.md) · GitHub: #8 · Phase 1 · Agent: backend-agent

## 1. Description
`TranscriptionQueue` ist ein unbegrenzter In-Memory-`Channel`. Stürzt die API ab oder wird sie neu gestartet, gehen alle eingereihten Jobs verloren. In der Datenbank bleiben sie für immer auf `Pending` oder `Processing` hängen. Die Datenbank ist die Quelle der Wahrheit: Beim Start werden offene Jobs erneut eingereiht.

## 2. User Stories
- Als Nutzer möchte ich, dass mein Upload nach einem Server-Neustart trotzdem transkribiert wird, damit ich nicht erneut hochladen muss.
- Als Nutzer möchte ich einen klaren Fehlerstatus sehen, wenn die Datei nach dem Neustart nicht mehr vorhanden ist.

## 3. API & Backend Requirements
- **Modell:** `AudioJob` bekommt ein Feld `FilePath` (string, nullable) oder leitet den Pfad deterministisch aus `Id` und Endung ab. Aktuell existiert der Pfad nur in `TranscriptionJobRequest`. → neue Migration.

## 4. Tasks

### S02-T1 Dateipfad am Job persistieren · backend · S
- `AudioJob.StoredFilePath` (oder `StoredFileName` relativ zu `TempStoragePath`) plus EF-Konfiguration und neue Migration. Bestehende Migrationen werden nicht verändert.
- **AC:**
  - [ ] Upload-Integrationstest prüft, dass der Pfad gespeichert wird.

### S02-T2 `JobRecoveryService` (IHostedService) · backend · M
- Läuft beim Start vor `TranscriptionWorker`, oder der Worker ruft die Wiederherstellung zu Beginn von `ExecuteAsync` auf:
  1. `Processing` → zurück auf `Pending` (der Lauf wurde abgebrochen).
  2. Alle `Pending`-Jobs werden nach `CreatedAtUtc` aufsteigend in die Queue gestellt.
  3. Existiert die Datei nicht, wird der Job `Failed` mit `ErrorMessage` „Quelldatei nach Neustart nicht mehr vorhanden“ (Text über i18n-Key im Frontend auflösbar).
- **AC (TDD):**
  - [ ] Test: 2× `Pending` und 1× `Processing` in der DB → 3 Einträge in der Queue, Reihenfolge nach Erstellzeit.
  - [ ] Test: Fehlende Datei → Status `Failed`, kein Queue-Eintrag.
  - [ ] Test: `Completed`- und `Failed`-Jobs werden ignoriert.

### S02-T3 Doppelverarbeitung verhindern · backend · S
- Der Worker prüft vor der Verarbeitung, ob der Job noch `Pending` ist. Andernfalls überspringt er ihn, z. B. wenn die Wiederherstellung und ein gleichzeitiger Upload denselben Job einreihen.
- **AC:**
  - [ ] Test: Ein bereits `Completed`-Job in der Queue wird nicht erneut transkribiert.

### S02-T4 Frontend-Status nach Neustart · frontend · XS
- Die SignalR-Verbindung baut sich nach einem API-Neustart neu auf (`withAutomaticReconnect`) und lädt die Job-Liste nach, damit die Status aktuell sind.
- **AC:**
  - [ ] Nach einem API-Neustart zeigt die Liste ohne manuellen Reload den korrekten Status.

## 5. Acceptance Criteria (DoD)
- [ ] Manueller Test: Upload → API während `Processing` beenden → neu starten → Job wird `Completed`.
