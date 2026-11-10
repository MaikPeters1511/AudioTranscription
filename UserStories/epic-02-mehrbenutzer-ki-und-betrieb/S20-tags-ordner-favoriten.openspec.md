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

## 4a. Umsetzung Backend (PR zu S20, Frontend folgt in einem zweiten PR)
- Die Pfade liegen unter `/api/audio-jobs/{id}/...` (nicht `/api/jobs`), wie der Rest der API.
- **Persönlich, auch bei geteilten Jobs (S19):** Tags und Favoriten gehören dem jeweiligen Benutzer, ein Leser darf einen geteilten Job mit eigenen Tags versehen und als Favorit markieren, ohne dass der Eigentümer das sieht. Ordner sind die Struktur des Eigentümers: nur er legt Jobs ab (`PUT /folder` verlangt Eigentümer, Freigegebene bekommen 403); ein geteilter Job taucht in keinem Ordner des Lesers auf.
- Endpoints: `GET/POST/DELETE /api/tags`, `PUT /api/audio-jobs/{id}/tags` (ersetzt die eigenen Tags am Job), `PUT/DELETE /api/audio-jobs/{id}/favorite`, `GET/POST/PUT/DELETE /api/folders`, `PUT /api/audio-jobs/{id}/folder`, `GET /api/audio-jobs?tag=&folder=&favorite=` (alle Bedingungen gelten gemeinsam), `GET /api/search?q=&tag=&folder=`.
- Regeln: Tag-Namen 1 bis 50 Zeichen, eindeutig pro Benutzer ohne Beachtung der Groß-/Kleinschreibung; Farbe nur `#RRGGBB` (so kann sie nie Markup enthalten); Ordnernamen 1 bis 100 Zeichen, eindeutig innerhalb des Elternordners; keine Zyklen beim Verschieben; ein fremder Tag oder Ordner passt auf nichts und verrät nichts. Der Ordnerfilter zeigt nur Jobs direkt im Ordner (nicht in Unterordnern).
- Löschen eines Ordners: Jobs wandern auf die oberste Ebene, Unterordner eine Ebene nach oben (bei Namenskonflikt mit Zusatz „(2)“). Löschen eines Tags entfernt ihn von allen Jobs.
- Migration `AddJobOrganization` (mit `dotnet ef` erzeugt), Daten: `Tags`, `JobTags`, `Folders`, `JobFavorites`, `AudioJobs.FolderId`.

## 5. Acceptance Criteria (DoD)
- [ ] Filter funktionieren einzeln und kombiniert, Berechtigungen aus S19 gelten.
- [ ] Swagger und E2E-Test für Tag-Filter.
