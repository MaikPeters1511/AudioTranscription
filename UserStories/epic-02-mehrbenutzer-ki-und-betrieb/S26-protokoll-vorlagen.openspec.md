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

## 7. Umsetzung

- **Modell:** `PostprocessingTemplate` (Name, Beschreibung, Prompt, Ausgabeschema, `IsBuiltIn`), die drei eingebauten Vorlagen werden per Migration `AddPostprocessingTemplates` mit festen Ids angelegt. Eine Vorlagen-Variante ist eine normale `TranscriptVariant` mit dem neuen Modus `Template` und `TemplateId` (eine Variante je Job und Vorlage, ein erneutes Erzeugen überschreibt sie). Wird die Vorlage gelöscht, bleiben ihre Varianten erhalten und verlieren nur die Verknüpfung.
- **Erzeugung (T1):** `LlmTemplateVariantGenerator` setzt Prompt (Platzhalter gefüllt), Schema und das als Daten abgegrenzte Transkript (`<transcript>`-Block, Anweisungen darin werden ignoriert, vgl. S24-T1) zusammen, liest das JSON tolerant aus der Antwort, prüft es gegen das Schema (`TemplateSchemaValidator`: object, array, string, integer, number, boolean, required, items, enum, weitere Felder erlaubt) und rendert Markdown (`TemplateOutput`). Ungültige Ausgabe oder ein leeres Ergebnis ist der definierte Fehlerzustand (`Failed` mit fester, technikfreier Meldung). Die Retry-Policy entspricht der der übrigen Varianten.
- **Sprecher:** Nur Sprecher mit gewähltem Namen (S11) gelten als benannt. Mit benannten Sprechern bekommt das Modell das Transkript mit Namen vor jeder Wortmeldung, und ein `owner` im Ergebnis bleibt nur erhalten, wenn er einem benannten Sprecher entspricht (Schreibweise des Sprechers); sonst wird er entfernt. Ohne benannte Sprecher gibt es keine Verantwortlichen.
- **Verwaltung (T2):** `GET /api/templates` für alle Angemeldeten, `POST/PUT/DELETE` nur Admin. Validierung: Name (Pflicht, eindeutig, 100 Zeichen), Prompt (Pflicht, 4000 Zeichen, nur bekannte Platzhalter), Schema (gültiges JSON, bekannte Typen, 8000 Zeichen). Eingebaute Vorlagen sind nicht änderbar oder löschbar (`409`).
- **Frontend (T3):** Vorlagenauswahl samt „Vorlage anwenden“ im Nachbearbeitungs-Menü der Job-Ansicht (Ergebnis erscheint als eigener Tab mit dem Vorlagennamen), Admin-Seite `/admin/templates` mit Liste und Formular (Servermeldungen an den Feldern, Löschen mit Rückfrage), DE/EN, Labels und `role="alert"`.
- **Export (DoD):** Das Ergebnis ist eine normale Variante und lässt sich wie jede andere über die Exporte aus S21 (Markdown, DOCX, ...) herunterladen.
- **Nicht umgesetzt/offen:** Lange Transkripte werden ohne Map-Reduce in einem Aufruf an das Modell gegeben (wie beim Modus „Bereinigt“); passt ein sehr langes Transkript nicht in den Kontext des Modells, scheitert die Erzeugung mit der festen Fehlermeldung. Das Ausgabeschema ist eine bewusste Teilmenge von JSON Schema, keine vollständige Bibliothek. Die Qualität der eingebauten Prompts mit einem echten Modell ist nicht gemessen, getestet ist mit Fake-LLM.
