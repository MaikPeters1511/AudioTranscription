# Story S30: Aufbewahrungsfristen (Retention)

> Epic: [E-02](./00-epic.openspec.md) · Phase 1 · Agenten: backend-agent, sec-agent · Größe: M

## 1. Description
Das Projekt wirbt mit Datenschutz. Neben dem Löschen der Audiodatei nach der Transkription (`Upload:DeleteAfterTranscription`) sollen auch Transkripte nach einer konfigurierbaren Frist automatisch gelöscht werden können.

## 2. User Stories
- Als Admin möchte ich eine Aufbewahrungsfrist festlegen, damit Daten nicht länger als nötig gespeichert bleiben.
- Als Benutzer möchte ich einzelne Jobs vom automatischen Löschen ausnehmen, damit wichtige Transkripte erhalten bleiben.

## 3. API & Backend Requirements
- **Konfiguration:** `Retention:TranscriptDays` (0 = aus, Standard), `Retention:AudioDays`, `Retention:RunIntervalHours`.
- **Modell:** `AudioJob.RetentionLocked` (bool).
- **Endpoints:** `PUT /api/jobs/{id}/retention-lock`.
- Hintergrunddienst nach dem Muster von `OrphanedUploadCleanupService`.

## 4. Tasks

### S30-T1 Konfiguration und Hintergrunddienst · backend · M
- **AC (TDD):**
  - [ ] Tests mit simulierter Uhr: Jobs älter als Frist werden mit Segmenten, Varianten, Sprechern und Dateien gelöscht, gesperrte Jobs bleiben.
  - [ ] Läuft idempotent, Fehler bei einem Job stoppen den Lauf nicht, es gibt keinen leeren `catch`.

### S30-T2 Such- und Index-Konsistenz · backend · S
- **AC:**
  - [ ] Gelöschte Jobs erscheinen nicht mehr in der Suche (Integrationstest).

### S30-T3 Frontend: Sperre und Hinweis · frontend · S
- **AC:**
  - [ ] Schloss-Schalter im Job, Hinweis „wird am … gelöscht“, DE/EN, ARIA-Label.

### S30-T4 Audit-Log · backend, sec · S
- **AC:**
  - [ ] Automatische Löschungen werden mit Job-ID und Zeitpunkt geloggt, ohne Inhalte.

## 4a. Umsetzung
- `RetentionService` (BackgroundService) mit `RunOnceAsync` für Tests mit simulierter Uhr; Optionen `Retention:TranscriptDays`, `Retention:AudioDays`, `Retention:RunIntervalHours`, alles standardmäßig aus.
- Maßgeblich ist `CompletedAtUtc`, ersatzweise `CreatedAtUtc`. Nur fertige, fehlgeschlagene und abgebrochene Jobs verfallen, offene nie.
- Folge-Dateien (Segmente, Fassungen, Sprecher, Freigaben) entfernt die Datenbank per Cascade. Betroffene Benutzer erhalten `JobDeleted`.
- Sperre: `PUT /api/audio-jobs/{id}/retention-lock` (nur Eigentümer, `AudioJobs.RetentionLocked`, Migration `AddRetentionLock` von Hand geschrieben, da `dotnet ef` in der Sandbox nicht läuft). Die Detailansicht nennt das Löschdatum (`deleteAtUtc`).
- T2 (Suche): Gelöschte Jobs verschwinden aus der Suche, weil die Volltextindizes auf den gelöschten Zeilen liegen. Ein eigener Suchtest braucht den SQL-Server-Container und läuft nur in der CI (offen).
- Hinweis zu mehreren Instanzen: Läuft die API mehrfach, laufen mehrere Retention-Dienste parallel; das Löschen ist idempotent, ein Job wird höchstens zweimal versucht.

## 5. Acceptance Criteria (DoD)
- [ ] Standard bleibt „keine automatische Löschung“, Dokumentation in README.
- [ ] Löschvorgang ist durch Tests abgesichert, Swagger aktualisiert.
