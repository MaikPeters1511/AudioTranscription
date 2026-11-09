# Story S18: Benutzerverwaltung und Rollen (Admin-UI)

> Epic: [E-02](./00-epic.openspec.md) · Phase 1 · Agenten: sec-agent, backend-agent, frontend-agent · Größe: L · Voraussetzung: S17

## 1. Description
Der Initial-Account erhält die Rolle `Admin` (siehe README), es gibt aber keine Oberfläche, um weitere Benutzer anzulegen, zu deaktivieren oder Rollen zu vergeben. Die Selbst-Registrierung ist standardmäßig aus.

## 2. User Stories
- Als Admin möchte ich Benutzer anlegen, deaktivieren und Rollen vergeben, damit ich den Zugang zur App kontrolliere.
- Als Benutzer möchte ich mein Passwort selbst ändern, damit ich es nicht beim Admin erfragen muss.

## 3. API & Backend Requirements
- **Autorisierung:** Alle Endpoints unter `/api/admin/users` nur mit Rolle `Admin`.
- **Endpoints:**
  - `GET /api/admin/users`: paginierte Liste (E-Mail, Rollen, Status, letzter Login).
  - `POST /api/admin/users`: Benutzer anlegen mit initialem Passwort oder Einladungslink.
  - `PUT /api/admin/users/{id}/roles`: Rollen setzen.
  - `POST /api/admin/users/{id}/deactivate` und `/activate`.
  - `POST /api/auth/change-password`: eigenes Passwort ändern.
- **Regeln:** Der letzte aktive Admin kann sich nicht selbst degradieren oder deaktivieren. Alle Admin-Aktionen werden mit Zeitpunkt und Akteur geloggt.

## 4. Tasks

### S18-T1 Backend: Rollenmodell und Admin-Endpoints · backend, sec · M
- **AC (TDD):**
  - [ ] Integrationstests für Berechtigung (403 ohne Admin), „letzter Admin“-Schutz, Deaktivierung beendet bestehende Sitzungen.
  - [ ] Keine Passwörter oder Hashes in Antworten oder Logs.

### S18-T2 Backend: Passwort ändern · backend, sec · S
- **AC:**
  - [ ] Altes Passwort wird verlangt, Identity-Passwortregeln gelten, Fehler liefern saubere Meldungen.

### S18-T3 Frontend: Benutzerverwaltung · frontend, ux · M
- **Komponenten:** Tabelle, Modal-Dialog zum Anlegen, Rollen-Auswahl, Bestätigungsdialog.
- **AC:**
  - [ ] Route nur für Admins sichtbar und per Guard geschützt, vollständig per Tastatur bedienbar, ARIA-Labels, DE/EN-Texte.
  - [ ] Unit-Tests für Guard und Formular-Validierung.

### S18-T4 Frontend: „Passwort ändern“ im Profil · frontend · S
- **AC:**
  - [ ] Formular mit Validierung, Erfolgs- und Fehlerhinweis, DE/EN.

## 5. Acceptance Criteria (DoD)
- [ ] Admin kann Benutzer verwalten, normale Benutzer erhalten auf allen Admin-Endpoints 403.
- [ ] OWASP-Prüfung durch `sec-agent` (Brute-Force-Schutz, Enumeration-Schutz bei Login).
- [ ] Swagger dokumentiert die Endpoints, E2E-Test für Anlegen und Login des neuen Benutzers.
