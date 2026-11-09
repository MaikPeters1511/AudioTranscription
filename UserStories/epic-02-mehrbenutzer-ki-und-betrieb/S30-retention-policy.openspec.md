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

## 5. Acceptance Criteria (DoD)
- [ ] Standard bleibt „keine automatische Löschung“, Dokumentation in README.
- [ ] Löschvorgang ist durch Tests abgesichert, Swagger aktualisiert.
