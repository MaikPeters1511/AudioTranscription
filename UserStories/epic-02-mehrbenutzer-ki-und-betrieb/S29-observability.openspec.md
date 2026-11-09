# Story S29: Observability

> Epic: [E-02](./00-epic.openspec.md) · Phase 1 · Agenten: devops-agent, backend-agent · Größe: M

## 1. Description
Aspire liefert Telemetrie-Grundlagen (`ServiceDefaults`). Es fehlen fachliche Metriken: Wie schnell transkribieren wir, wie lang ist die Queue, wie hoch ist die Fehlerquote?

## 2. User Stories
- Als Betreiber möchte ich Echtzeitfaktor, Queue-Länge und Fehlerquote sehen, damit ich Engpässe früh erkenne.

## 3. Tasks

### S29-T1 Fachliche Metriken · backend · M
- Metriken über `System.Diagnostics.Metrics`: `transcription.jobs.completed/failed`, `transcription.duration`, `transcription.realtime_factor`, `transcription.queue.length`, Nachbearbeitungs-Dauer.
- **AC (TDD):**
  - [ ] Unit-Tests prüfen, dass Metriken bei Erfolg/Fehler/Abbruch gesetzt werden.
  - [ ] Keine personenbezogenen Daten (Dateinamen, Inhalte) in Labels.

### S29-T2 Traces für die Pipeline · backend · S
- **AC:**
  - [ ] Ein Trace pro Job mit Spans für Konvertierung, Whisper, Diarisierung und Nachbearbeitung.

### S29-T3 Dashboard und Alarme · devops · M
- **AC:**
  - [ ] Aspire-Dashboard zeigt die Metriken; optional Prometheus/Grafana per docker-compose-Profil mit vorbereitetem Dashboard (JSON im Repo).
  - [ ] Dokumentierte Alarmschwellen (Fehlerquote, Queue-Alter).

### S29-T4 Health-Checks erweitern · devops, backend · S
- **AC:**
  - [ ] `/health` prüft Datenbank, ffmpeg, Whisper-Modell und (falls aktiv) Ollama/Qdrant, ohne sensible Details nach außen.

## 4. Acceptance Criteria (DoD)
- [ ] Metriken sind im Aspire-Dashboard sichtbar, README beschreibt die Nutzung.
