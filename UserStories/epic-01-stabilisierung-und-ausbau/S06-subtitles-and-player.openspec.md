# Story S06: Zeitstempel-Segmente, Untertitel-Export (SRT/VTT) und synchroner Player

> Epic: [E-01](./00-epic.openspec.md) · Phase 2 · Agenten: backend-agent, frontend-agent, ux-agent

## 1. Description
Whisper liefert jedes Segment mit `Start`/`End`. `WhisperTranscriptionService` hängt aber nur `segment.Text` an und verwirft die Zeitstempel. Wenn wir die Segmente speichern, sind möglich:
1. Download als `.srt` oder `.vtt`.
2. Ein Audio-Player mit synchronem Transkript: Ein Klick auf einen Satz springt zur passenden Stelle, der aktuell gesprochene Satz wird hervorgehoben.

## 2. User Stories
- Als Nutzer möchte ich Untertitel als SRT oder VTT herunterladen, damit ich sie in Videoschnitt- oder Player-Software verwenden kann.
- Als Nutzer möchte ich beim Anhören den aktuell gesprochenen Satz hervorgehoben sehen und per Klick auf einen Satz dorthin springen.

## 3. API & Backend Requirements
- **Entität `TranscriptSegment`:** `Id` (Guid), `AudioJobId` (FK, Cascade Delete), `Index` (int), `StartMs` (long), `EndMs` (long), `Text` (string). Index auf (`AudioJobId`, `Index`).
- **Endpoints:**
  - `GET /api/audio-jobs/{id}/segments` → Liste (für den Player).
  - `GET /api/audio-jobs/{id}/subtitles?format=srt|vtt` → Datei-Download (`application/x-subrip` bzw. `text/vtt`).
  - `GET /api/audio-jobs/{id}/audio` → Audio-Stream mit HTTP-Range-Unterstützung (`enableRangeProcessing`). Setzt die Aufbewahrung der Datei voraus (D2), sonst `410 Gone`.
- Die Segmente beziehen sich auf das **Roh-Transkript**. Die Nachbearbeitung (S04/S10) ändert keine Zeitstempel.

## 4. Tasks

### S06-T1 Segmente aus Whisper zurückgeben · backend · S
- `TranscriptionResult` wird um `IReadOnlyList<SegmentResult>` erweitert.
- **AC:**
  - [ ] Test: Anzahl und Zeitstempel werden unverändert durchgereicht.

### S06-T2 Entität, EF-Konfiguration und Migration · backend · S
- **AC:**
  - [ ] Test: Beim Löschen eines Jobs werden seine Segmente mitgelöscht.

### S06-T3 Worker speichert Segmente · backend · XS
- **AC:**
  - [ ] Integrationstest: Nach Abschluss sind die Segmente in der DB.

### S06-T4 Reiner `SubtitleFormatter` (SRT und VTT) · backend · S
- Domain- oder Application-Service ohne I/O. Zeitformat SRT `HH:MM:SS,mmm`, VTT `HH:MM:SS.mmm` mit `WEBVTT`-Header. Zeilen werden nach ca. 42 Zeichen umbrochen.
- **AC (TDD, zuerst rot):**
  - [ ] Snapshot-Tests für beide Formate.
  - [ ] Randfälle: 0 ms, über 1 Stunde, Sonderzeichen, leere Segmente werden übersprungen.

### S06-T5 Endpoints `/segments` und `/subtitles` · backend · S
- `Content-Disposition` mit sicherem Dateinamen, abgeleitet von `FileName`, sanitisiert. `404` bei unbekanntem Job, `409`, wenn der Job nicht `Completed` ist.
- **AC:**
  - [ ] Integrationstests und Swagger.

### S06-T6 Endpoint `/audio` mit Range-Support · backend · S
- **AC:**
  - [ ] Integrationstest: Ein `Range`-Header liefert `206`.
  - [ ] Fehlende Datei liefert `410`.

### S06-T7 Frontend: Download-Buttons SRT/VTT · frontend · XS
- **AC:**
  - [ ] Buttons sind nur bei `Completed` sichtbar, haben i18n-Labels und sind per Tastatur bedienbar.

### S06-T8 Frontend: Player mit synchronem Transkript · frontend · M
- Natives `<audio controls>` und eine Segmentliste als `<button>`-Elemente (fokussierbar). `timeupdate` → aktives Segment als Signal, per Binärsuche ermittelt. Das aktive Segment wird mit `aria-current="true"` markiert und in den sichtbaren Bereich gescrollt (`prefers-reduced-motion` beachten). Klick oder Enter setzt `currentTime`.
- **AC:**
  - [ ] Unit-Test: Die Zeit-zu-Segment-Zuordnung funktioniert.
  - [ ] Unit-Test: Klick setzt `currentTime`.
  - [ ] Ist kein Audio vorhanden (410), wird nur die Segmentliste mit Zeitstempeln angezeigt.

### S06-T9 UX- und a11y-Review des Players · ux · XS
- **AC:**
  - [ ] Review-Ergebnis ist dokumentiert und Findings sind umgesetzt.

## 5. Acceptance Criteria (DoD)
- [ ] Die exportierte SRT-Datei lässt sich in VLC fehlerfrei laden und ist synchron.
- [ ] Der Player springt bei Klick auf ein Segment auf ±0,5 s genau.
