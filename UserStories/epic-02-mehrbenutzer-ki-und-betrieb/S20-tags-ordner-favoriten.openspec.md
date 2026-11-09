# Story S20: Tags, Ordner und Favoriten

> Epic: [E-02](./00-epic.openspec.md) · Phase 2 · Agenten: backend-agent, frontend-agent, ux-agent · Größe: M · Voraussetzung: S19

## 1. Description
Mit wachsender Zahl an Transkripten wird die Jobliste unübersichtlich. Benutzer ordnen ihre Jobs mit Tags, Ordnern und Favoriten.

## 2. User Stories
- Als Benutzer möchte ich Jobs mit Tags versehen und nach ihnen filtern, damit ich Zusammengehöriges finde.
- Als Benutzer möchte ich Jobs in Ordner legen, damit ich Projekte trennen kann.
- Als Benutzer möchte ich Jobs als Favorit markieren, damit ich sie schnell wieder öffne.

## 3. API & Backend Requirements
- **Modelle (neue Migration):** `Tag` (Id, OwnerId, Name, Farbe), `JobTag`, `Folder` (Id, OwnerId, Name, ParentId), `AudioJob.FolderId`, `JobFavorite` (JobId, UserId).
- **Endpoints:** `GET/POST/DELETE /api/tags`, `PUT /api/jobs/{id}/tags`, `GET/POST/PUT/DELETE /api/folders`, `PUT /api/jobs/{id}/folder`, `PUT/DELETE /api/jobs/{id}/favorite`, `GET /api/jobs?tag=&folder=&favorite=`.
- Tags und Ordner gehören dem Benutzer (nicht geteilt in dieser Story).

## 4. Tasks

### S20-T1 Backend: Tags und Favoriten · backend · M
- **AC (TDD):** Tests für Anlegen, Zuweisen, Filtern, eindeutige Namen pro Benutzer, Löschen eines Tags entfernt Zuordnungen.

### S20-T2 Backend: Ordner · backend · M
- **AC (TDD):** Tests für Hierarchie (keine Zyklen), Verschieben, Löschen mit Jobs (Jobs wandern in die Wurzel).

### S20-T3 Frontend: Filter und Zuweisung · frontend, ux · M
- **Komponenten:** Tag-Chips (DaisyUI `badge`), Ordnerbaum, Stern-Toggle, Filterleiste.
- **AC:** Tastaturbedienbar, ARIA (`aria-pressed` beim Favoriten), DE/EN, Unit-Tests für Filter-State.

### S20-T4 Suche kombinieren · backend, frontend · S
- **AC:** Suche (S13) lässt sich auf Tag/Ordner einschränken.

## 5. Acceptance Criteria (DoD)
- [ ] Filter funktionieren einzeln und kombiniert, Berechtigungen aus S19 gelten.
- [ ] Swagger und E2E-Test für Tag-Filter.
