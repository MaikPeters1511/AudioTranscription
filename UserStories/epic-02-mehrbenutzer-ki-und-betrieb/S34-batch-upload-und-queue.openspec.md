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

## 6. Umsetzung

- **Queue:** `TranscriptionQueue` ist jetzt eine In-Memory-Prioritätswarteschlange (höchste Priorität zuerst, bei Gleichstand FIFO nach Ankunft; Priorität wartender Jobs änderbar). Die Priorität steht zusätzlich am Job (`AudioJob.Priority`, Migration `AddQueuePriority`), damit sie einen Neustart überlebt: `JobRecoveryService` reiht nach `QueuedAtUtc` (sonst `CreatedAtUtc`) ein und gibt die Priorität mit.
- **Neue Felder:** `Priority`, `QueuedAtUtc` (gesetzt bei Upload und Retry) und `StartedAtUtc` (gesetzt vom Worker). Letzteres war in der Spec nicht genannt, wird aber für die Schätzung gebraucht (Verarbeitungszeit = `CompletedAtUtc - StartedAtUtc`).
- **Endpoints:** `GET /api/queue` und `PUT /api/audio-jobs/{id}/priority` (statt `/api/jobs/...`, passend zu den übrigen Job-Routen; Zugriff über `RequireJobAccess`, fremde Jobs → 404).
- **Schätzung:** Die Audiolänge wartender Jobs ist erst nach der Transkription bekannt, daher dient die Dateigröße als Näherung: Sekunden pro Byte aus den letzten 20 fertigen Jobs, angewandt auf den Rest des laufenden Jobs und alle Jobs davor. Ohne fertigen Job gibt es keine Schätzung (`null`, in der UI „–“).
- **Abbruch (S09):** Ein abgebrochener wartender Job bleibt bis zum Überspringen durch den Worker in der Queue, wird in der Übersicht aber ausgeblendet und die Positionen werden neu gezählt.
- **Frontend:** `<app-upload-batch>` (bis zu 3 parallele Uploads, Fortschritt und Abbrechen je Datei und für alle, Fehler einer Datei stoppen die anderen nicht, `aria-live`-Zusammenfassung); eine einzelne Datei behält den bisherigen Ablauf (Weiterleitung zum Job). Die Seite `/queue` (`QueueComponent`) aktualisiert sich alle 5 Sekunden.
- **Nicht gemessen/offen:** Der E2E-Test (`batch-queue.mocked.spec.ts`, 10 Dateien, Priorität, Abbruch) läuft nur in der CI, Playwright-Browser sind lokal nicht installiert. Eine Umsortierung per Drag & Drop gibt es nicht; die Priorität wird über Stufen (Niedrig/Normal/Hoch) gewählt.

