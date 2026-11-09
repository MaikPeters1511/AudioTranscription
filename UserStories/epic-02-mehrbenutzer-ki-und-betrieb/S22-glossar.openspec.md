# Story S22: Glossar für Fachbegriffe

> Epic: [E-02](./00-epic.openspec.md) · Phase 2 · Agenten: backend-agent, frontend-agent · Größe: M

## 1. Description
Whisper verschreibt sich bei Eigennamen und Fachbegriffen. Ein benutzerdefiniertes Glossar wird als Initial-Prompt an Whisper übergeben und verbessert die Erkennung.

## 2. User Stories
- Als Benutzer möchte ich Fachbegriffe und Namen hinterlegen, damit sie korrekt transkribiert werden.
- Als Benutzer möchte ich pro Upload ein Glossar auswählen, damit ich nur passende Begriffe verwende.

## 3. API & Backend Requirements
- **Modelle:** `Glossary` (Id, OwnerId, Name), `GlossaryTerm` (GlossaryId, Term).
- **Endpoints:** CRUD unter `/api/glossaries`; Upload akzeptiert optional `glossaryId`.
- Initial-Prompt wird auf die Whisper-Tokenlänge (ca. 224 Tokens) begrenzt, Überschuss wird dem Benutzer gemeldet statt still abgeschnitten.

## 4. Tasks

### S22-T1 Spike: Wirkung des Initial-Prompts · backend · S
- Vergleich mit/ohne Prompt an einer Beispieldatei, Ergebnis kurz dokumentiert.
- **AC:** Notiz im Epic oder ADR mit messbarem Unterschied und Empfehlung zur Formulierung.

### S22-T2 Backend: Glossar-CRUD · backend · M
- **AC (TDD):** Tests für Validierung (Länge, Duplikate), Besitzprüfung.

### S22-T3 Backend: Prompt in Whisper-Aufruf · backend · S
- **AC (TDD):** Der Prompt wird beim Job gespeichert (Nachvollziehbarkeit) und im Whisper-Aufruf gesetzt; Test mit Fake-Prozessor.

### S22-T4 Frontend: Glossar-Verwaltung und Auswahl · frontend, ux · M
- **AC:** Liste mit Begriffen, Import aus Textfeld (ein Begriff je Zeile), Auswahl im Upload-Dialog, DE/EN, ARIA.

## 5. Acceptance Criteria (DoD)
- [ ] Glossar wirkt nachweislich im Spike-Beispiel, Tests und Swagger sind aktuell.
