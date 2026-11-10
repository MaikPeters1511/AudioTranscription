# Story S25: Automatische Kapitel und Themen

> Epic: [E-02](./00-epic.openspec.md) · Phase 3 · Agenten: ai-agent, backend-agent, frontend-agent · Größe: M

## 1. Description
Lange Aufnahmen werden vom LLM in Abschnitte mit Überschrift und Kurzzusammenfassung gegliedert. Die Kapitel erscheinen als Marker im Player.

## 2. User Stories
- Als Benutzer möchte ich eine Kapitelübersicht meiner Aufnahme, damit ich direkt zu einem Thema springe.

## 3. API & Backend Requirements
- **Modell:** `TranscriptChapter` (JobId, StartMs, EndMs, Title, Summary).
- **Endpoints:** `POST /api/jobs/{id}/chapters` (erzeugen, asynchron), `GET /api/jobs/{id}/chapters`, `PUT/DELETE` zum manuellen Anpassen.
- Erzeugung nutzt die Variantenmechanik aus S10, lange Transkripte werden in Fenster geteilt (Map-Reduce).

## 4. Tasks

### S25-T1 Prompt und Fensterung · ai · M
- **AC (TDD):** Tests mit Fake-LLM für Fensterung, Zusammenführen überlappender Kapitel und defektes LLM-Format (robustes Parsing, definierter Fehlerzustand).

### S25-T2 Backend: Erzeugung und Speicherung · backend · M
- **AC:** Fortschritt und Abbruch wie bei Varianten, Kapitelgrenzen liegen auf Segmentgrenzen, Berechtigungen aus S19.

### S25-T3 Frontend: Kapitelleiste im Player · frontend, ux · M
- **AC:** Marker auf der Zeitleiste, Liste mit Sprungfunktion, per Tastatur erreichbar, DE/EN, Unit-Tests.

## 5. KI/AI Requirements
- **Vektorsuche (Qdrant):** Nein.
- **Prompt/System Message:** Gliedere in thematische Abschnitte, gib JSON mit Start-Segment, Titel, Zusammenfassung zurück; Sprache wie das Transkript.

## 6. Acceptance Criteria (DoD)
- [ ] Eine einstündige Beispielaufnahme erzeugt plausible Kapitel ohne Überlappung oder Lücken (Test mit Fake-Daten, manuelle Stichprobe dokumentiert).

## 7. Umsetzung

- **Modell:** `TranscriptChapter` (Position, StartMs, EndMs, Title, Summary) und `ChapterRun` (ein Lauf je Job mit Status wie bei Varianten; Migration `AddChapters`). Die Kapitel eines Jobs schließen lückenlos aneinander an: jedes endet dort, wo das nächste beginnt, die Grenzen liegen auf Segmentgrenzen (`ChapterBuilder`).
- **Erzeugung (T1/T2):** `LlmChapterGenerator` teilt die nummerierten Segmente in Fenster (`MaxChunkLength`, nie mitten in einem Segment), fragt je Fenster nach JSON, parst robust (`ChapterResponseParser`: Code-Zäune, umgebender Text, ungültige Einträge werden übersprungen) und führt die Antworten zusammen (`ChapterMerger`: sortieren, doppelte Starts, gleiche Titel an Fenstergrenzen, erstes Kapitel beginnt am ersten Segment). Ein unbrauchbares Fenster wird übersprungen; liefert kein Fenster Kapitel, ist der Lauf `Failed` mit fester, technikfreier Meldung. Die Erzeugung nutzt den korrigierten Segmenttext (S32) und die Retry-Policy wie die Varianten (S10).
- **Endpoints:** unter `/api/audio-jobs/{id}/chapters` (statt `/api/jobs/...`), Berechtigungen aus S19 (Lesen für alle mit Zugriff, Ändern nur Eigentümer). Neue Erzeugung ersetzt alle Kapitel inklusive handbearbeiteter. Ein laufender Lauf wird nicht doppelt gestartet; nach einem Neustart werden offene Läufe wieder eingereiht. SignalR-Event `ChaptersCompleted`.
- **Frontend (T3):** `<app-chapter-nav>` im Player: Marker auf der Zeitleiste (jeder ein Button mit `aria-label`), Liste mit Sprung, aktuelles Kapitel per `aria-current`, Umbenennen, Löschen mit Rückfrage, `role="status"`/`role="alert"`; Leser sehen die Leiste nur, wenn es Kapitel gibt. DE/EN.
- **Nicht umgesetzt/offen:** Es gibt keinen Fortschrittsbalken und keinen Abbruch einer laufenden Erzeugung (wie bei Varianten nur Status). Kapitelgrenzen lassen sich nicht von Hand verschieben, nur Text ändern oder löschen. Die manuelle Stichprobe mit einer echten einstündigen Aufnahme und einem echten Modell steht aus, getestet ist die einstündige Aufnahme mit Fake-LLM (720 Segmente, lückenlos). `chapters.mocked.spec.ts` läuft nur in der CI.
