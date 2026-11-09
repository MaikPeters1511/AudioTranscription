# Story S26: Meeting-Protokoll-Vorlagen

> Epic: [E-02](./00-epic.openspec.md) · Phase 3 · Agenten: ai-agent, backend-agent, frontend-agent · Größe: M

## 1. Description
Die Nachbearbeitung (S10) kennt Zusammenfassung, Stichpunkte und Aufgabenliste. Protokoll-Vorlagen erzeugen strukturierte Ergebnisse: Beschlüsse, Aufgaben mit Verantwortlichen (aus den Sprechern) und Termine.

## 2. User Stories
- Als Benutzer möchte ich aus einer Besprechung ein Protokoll mit Beschlüssen und Aufgaben erzeugen, damit ich es direkt verschicken kann.
- Als Admin möchte ich eigene Vorlagen definieren, damit das Protokoll zum Team-Format passt.

## 3. API & Backend Requirements
- **Modell:** `PostprocessingTemplate` (Id, Name, Prompt, OutputSchema, IsBuiltIn).
- **Endpoints:** `GET /api/templates`, `POST/PUT/DELETE /api/templates` (Admin für globale Vorlagen), Variante erzeugen mit `templateId`.
- Strukturierte Ausgabe wird validiert (JSON-Schema) und als Markdown gerendert.

## 4. Tasks

### S26-T1 Eingebaute Vorlagen · ai · S
- Vorlagen: Besprechungsprotokoll, Entscheidungsliste, Aufgaben mit Verantwortlichen.
- **AC (TDD):** Tests mit Fake-LLM prüfen Schema-Validierung, Sprecherzuordnung und definierten Fehlerzustand bei ungültiger Ausgabe.

### S26-T2 Backend: Vorlagenverwaltung · backend · M
- **AC:** CRUD mit Validierung (Prompt-Länge, Platzhalter), eingebaute Vorlagen sind nicht löschbar.

### S26-T3 Frontend: Auswahl und Editor · frontend, ux · M
- **AC:** Vorlagenauswahl im Nachbearbeitungs-Menü, einfacher Editor für eigene Vorlagen, DE/EN, ARIA.

## 5. KI/AI Requirements
- **Vektorsuche (Qdrant):** Nein.
- **Prompt/System Message:** Aus Vorlage plus Transkript; Transkript klar als Daten abgegrenzt (siehe S24-T1).

## 6. Acceptance Criteria (DoD)
- [ ] Ergebnis ist als Markdown/DOCX exportierbar (S21) und enthält Verantwortliche nur, wenn Sprecher benannt sind.
