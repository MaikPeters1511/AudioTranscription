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
