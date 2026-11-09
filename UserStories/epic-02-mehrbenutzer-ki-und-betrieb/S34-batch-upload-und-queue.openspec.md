# Story S34: Batch-Upload und Warteschlangen-Übersicht

> Epic: [E-02](./00-epic.openspec.md) · Phase 2 · Agenten: backend-agent, frontend-agent, ux-agent · Größe: M

## 1. Description
Mehrere Dateien sollen auf einmal hochgeladen werden können. Eine Warteschlangen-Übersicht zeigt, was läuft und was wartet, mit Priorisierung und Abbruch.

## 2. User Stories
- Als Benutzer möchte ich mehrere Dateien gleichzeitig hochladen, damit ich nicht jede einzeln anstoßen muss.
- Als Benutzer möchte ich die Reihenfolge der wartenden Jobs ändern, damit Dringendes zuerst läuft.

## 3. API & Backend Requirements
- **Modelle:** `AudioJob.Priority` (int, Standard 0), `AudioJob.QueuedAt`.
- **Endpoints:** `GET /api/queue` (Position, geschätzte Wartezeit), `PUT /api/jobs/{id}/priority`.
- Upload bleibt pro Datei ein Request (Streaming), das Frontend verarbeitet mehrere parallel mit Limit (z. B. 3).

## 4. Tasks

### S34-T1 Backend: Priorität und Queue-Reihenfolge · backend · M
- **AC (TDD):** Höhere Priorität wird zuerst bearbeitet, bei Gleichstand FIFO; Wiederherstellung (S02) behält die Reihenfolge.

### S34-T2 Backend: Queue-Endpoint · backend · S
- **AC:** Liefert Position und Schätzung auf Basis des durchschnittlichen Echtzeitfaktors, nur eigene Jobs sichtbar (S19).

### S34-T3 Frontend: Mehrfach-Upload · frontend, ux · M
- **AC:** Mehrfachauswahl und Drag & Drop, Fortschritt je Datei, Fehler bei einer Datei stoppen die anderen nicht, Tastatur und ARIA-Live-Region.

### S34-T4 Frontend: Queue-Ansicht · frontend · S
- **AC:** Position, Priorität ändern, Abbrechen (nutzt S09), DE/EN.

## 5. Acceptance Criteria (DoD)
- [ ] 10 Dateien lassen sich in einem Zug hochladen, Reihenfolge und Abbruch funktionieren (E2E).
