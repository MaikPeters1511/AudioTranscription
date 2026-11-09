# Story S19: Jobs pro Benutzer und Freigaben

> Epic: [E-02](./00-epic.openspec.md) · Phase 1 · Agenten: architect-agent, sec-agent, backend-agent, frontend-agent · Größe: L · Voraussetzung: S18, Entscheidung D6

## 1. Description
Bisher sehen alle angemeldeten Benutzer dieselben Jobs (ADR 0003). Künftig besitzt jeder Job einen Eigentümer. Standard ist privat, einzelne Jobs lassen sich an andere Benutzer freigeben.

## 2. User Stories
- Als Benutzer möchte ich, dass nur ich meine Transkripte sehe, damit vertrauliche Aufnahmen privat bleiben.
- Als Benutzer möchte ich einen Job mit Kollegen teilen (lesend), damit wir gemeinsam am Transkript arbeiten können.
- Als Admin möchte ich bestehende Jobs einem Benutzer zuordnen, damit die Altdaten nicht verwaist sind.

## 3. API & Backend Requirements
- **Modelle (SQL Server, neue Migration):** `AudioJob.OwnerId` (FK auf Benutzer), `JobShare` (`JobId`, `UserId`, `Permission` = Read).
- **Endpoints:**
  - `GET /api/jobs`: liefert nur eigene und geteilte Jobs.
  - `POST /api/jobs/{id}/shares`, `DELETE /api/jobs/{id}/shares/{userId}`.
  - `POST /api/admin/jobs/{id}/owner`: Besitzer setzen (Admin).
- **Querschnitt:** Zentrale Zugriffsprüfung (Policy/Handler), keine verteilten Einzelchecks. SignalR sendet Updates nur an berechtigte Benutzer. Suche (S13) berücksichtigt Berechtigungen.

## 4. Tasks

### S19-T1 ADR: Berechtigungsmodell · architect · S
- **AC:**
  - [ ] ADR in `docs/adr/` legt Eigentümer-/Freigabe-Modell, Policy-Struktur und SignalR-Gruppen fest (D6).

### S19-T2 Migration mit Altdaten-Zuordnung · backend · S
- **AC:**
  - [ ] Neue Migration (bestehende nicht ändern). Bestehende Jobs werden dem ersten Admin zugeordnet.

### S19-T3 Zentrale Autorisierung für Jobs · backend, sec · M
- **AC (TDD):**
  - [ ] Tests: fremder Job → 404 (nicht 403, um Existenz nicht zu verraten), geteilter Job lesbar, nicht änderbar/löschbar.
  - [ ] Alle bestehenden Job-Endpoints (Detail, Segmente, Varianten, Sprecher, Export, Audio) prüfen die Policy.

### S19-T4 SignalR und Suche absichern · backend, sec · M
- **AC:**
  - [ ] Live-Updates erreichen nur Eigentümer und Freigegebene; Suchtreffer enthalten nur berechtigte Jobs (Integrationstest).

### S19-T5 Frontend: Freigabe-Dialog und Kennzeichnung · frontend, ux · M
- **AC:**
  - [ ] „Geteilt von …“ und „Geteilt mit …“ sind sichtbar, Dialog zum Hinzufügen/Entfernen, DE/EN, Tastatur und ARIA.

## 5. Acceptance Criteria (DoD)
- [ ] Ein Benutzer kann keine fremden, nicht freigegebenen Jobs lesen oder erraten (IDOR-Tests grün).
- [ ] Security-Review durch `sec-agent` ist abgeschlossen, E2E-Test mit zwei Benutzern ist grün.
