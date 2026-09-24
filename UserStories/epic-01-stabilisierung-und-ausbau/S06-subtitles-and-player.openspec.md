# Story S06: Zeitstempel-Segmente, Untertitel-Export (SRT/VTT) und synchroner Player

> Epic: [E-01](./00-epic.openspec.md) · GitHub: #13 · Phase 2 · Agenten: backend-agent, frontend-agent, ux-agent

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
  - [x] Test: Anzahl und Zeitstempel werden unverändert durchgereicht.

### S06-T2 Entität, EF-Konfiguration und Migration · backend · S
- **AC:**
  - [x] Test: Beim Löschen eines Jobs werden seine Segmente mitgelöscht.

### S06-T3 Worker speichert Segmente · backend · XS
- **AC:**
  - [x] Integrationstest: Nach Abschluss sind die Segmente in der DB.

### S06-T4 Reiner `SubtitleFormatter` (SRT und VTT) · backend · S
- Domain- oder Application-Service ohne I/O. Zeitformat SRT `HH:MM:SS,mmm`, VTT `HH:MM:SS.mmm` mit `WEBVTT`-Header. Zeilen werden nach ca. 42 Zeichen umbrochen.
- **AC (TDD, zuerst rot):**
  - [x] Snapshot-Tests für beide Formate.
  - [x] Randfälle: 0 ms, über 1 Stunde, Sonderzeichen, leere Segmente werden übersprungen.

### S06-T5 Endpoints `/segments` und `/subtitles` · backend · S
- `Content-Disposition` mit sicherem Dateinamen, abgeleitet von `FileName`, sanitisiert. `404` bei unbekanntem Job, `409`, wenn der Job nicht `Completed` ist.
- **AC:**
  - [x] Integrationstests. Swagger: Die API hat noch kein OpenAPI-Dokument. Die Endpoints tragen `WithName` und `WithDescription`.

### S06-T6 Endpoint `/audio` mit Range-Support · backend · S
- **AC:**
  - [x] Integrationstest: Ein `Range`-Header liefert `206`.
  - [x] Fehlende Datei liefert `410`.

### S06-T7 Frontend: Download-Buttons SRT/VTT · frontend · XS
- **AC:**
  - [x] Buttons sind nur bei `Completed` sichtbar, haben i18n-Labels und sind per Tastatur bedienbar.

### S06-T8 Frontend: Player mit synchronem Transkript · frontend · M
- Natives `<audio controls>` und eine Segmentliste als `<button>`-Elemente (fokussierbar). `timeupdate` → aktives Segment als Signal, per Binärsuche ermittelt. Das aktive Segment wird mit `aria-current="true"` markiert und in den sichtbaren Bereich gescrollt (`prefers-reduced-motion` beachten). Klick oder Enter setzt `currentTime`.
- **AC:**
  - [x] Unit-Test: Die Zeit-zu-Segment-Zuordnung funktioniert.
  - [x] Unit-Test: Klick setzt `currentTime`.
  - [x] Ist kein Audio vorhanden (410), wird nur die Segmentliste mit Zeitstempeln angezeigt.

### S06-T9 UX- und a11y-Review des Players · ux · XS
- **AC:**
  - [x] Review-Ergebnis ist dokumentiert und Findings sind umgesetzt.

## 5. Umsetzungsnotizen (2026-09-24)
- **Segmente (T1):** `TranscriptionResult.Segments` enthält `SegmentResult(Start, End, Text)`. `TranscriptionResultBuilder` sammelt die Whisper-Segmente, damit das ohne Modell testbar ist. Der Text jedes Segments wird getrimmt, der Gesamttext bleibt wie bisher die Verkettung der Segmente.
- **Entität (T2):** `TranscriptSegment` liegt ohne Navigation am `AudioJob`. Der Fremdschlüssel hat `ON DELETE CASCADE` und einen Index auf (`AudioJobId`, `Index`), Migration `AddTranscriptSegments`. Ein Test gegen SQL Server löscht einen Job, ohne die Segmente zu laden. Die Datenbank löscht sie mit, nicht der Change Tracker.
- **Worker (T3):** Er speichert die Segmente (in ms gerundet) zusammen mit dem Status `Completed` in einem `SaveChanges`. Nur abgeschlossene Jobs haben also Segmente. Ein Retry betrifft nur `Failed`/`Cancelled`-Jobs, die keine haben.
- **Formatter (T4):** `SubtitleFormatter` in `AudioTranscription.Domain/Subtitles`, ohne I/O. Er
  - sortiert nach `Index` und überspringt leere Segmente; SRT nummeriert fortlaufend neu.
  - fasst Whitespace zusammen und bricht Zeilen gierig bei 42 Zeichen um. Ein längeres Wort bleibt ganz.
  - zählt Stunden über 24 hinaus weiter, begrenzt negative Zeiten auf 0 und hebt ein Ende vor dem Start auf den Start an.
  - escaped in VTT `&`, `<`, `>`; so kann auch `-->` die Zeitzeile nicht stören. SRT bleibt unverändert.
  - Zeilenende ist `\n`, Kodierung UTF-8 ohne BOM.
- **Endpoints (T5, T6):**
  - `404` für unbekannte Jobs. `/segments` und `/subtitles` antworten mit `409`, solange der Job nicht `Completed` ist. `/subtitles` ohne oder mit unbekanntem `format` gibt `400`.
  - **Download-Name:** Er wird aus `FileName` abgeleitet: Nur Buchstaben, Ziffern, Leerzeichen, `-`, `_` und `.` bleiben erhalten, maximal 100 Zeichen, sonst `transcript`. ASP.NET setzt `filename` und `filename*` (UTF-8).
  - **Audio:** `/audio` liefert `PhysicalFile` mit Range-Support und `410`, wenn die Datei fehlt.
  - Alle drei Endpoints erfordern eine Anmeldung (Fallback-Policy). Das `<audio>`-Element und die Download-Links sind same-origin, der Browser schickt das Session-Cookie mit.
- **Hinweis zu D2:** Standardmäßig ist die Audiodatei nach erfolgreicher Transkription gelöscht (`Upload:DeleteAfterTranscription=true`). Der Player zeigt dann nur die Segmente mit Zeitstempeln. Das steht im README.
- **Frontend (T7):** SRT- und VTT-Links (`<a download>`) im Kopf der Transkript-Karte, nur bei `Completed`. Die ARIA-Labels („Untertitel als SRT herunterladen“) enthalten den sichtbaren Text (WCAG 2.5.3).
- **Frontend (T8):** `TranscriptPlayerComponent` mit
  - `<audio controls>` und einer `<ol>` mit Buttons. Das aktive Segment wird per Binärsuche ermittelt (`segment-time.ts`) und mit `aria-current="true"` markiert. In Pausen zwischen Segmenten ist keines aktiv.
  - Scrollen nur innerhalb der Liste, nicht der ganzen Seite. `prefers-reduced-motion` schaltet die Animation ab.
  - Ohne Audio (Fehler-Event, z. B. `410`) verschwinden Player und Buttons, die Segmente bleiben als Liste mit Zeitstempeln stehen.
- **Prüfung im echten Stack:** Mit API, SQL Server 2022, Angular-Dev-Proxy und Chromium ist geprüft:
  - Der Browser lädt das Audio per Range-Request (`bytes=0-`).
  - Ein Klick oder Enter auf ein Segment setzt `currentTime` exakt auf dessen Start (5,0 s bzw. 2,5 s).
  - Beim Suchen auf 8,2 s wird das richtige Segment hervorgehoben.
  - Die SRT- und VTT-Downloads haben den erwarteten Inhalt.
  - Nach Entfernen der Audiodatei erscheint der Hinweis mit der Segmentliste.
- **Beobachtung:** Headless-Chromium im Container speichert Dateien mit Umlaut im Namen als `download`. Ursache ist die fehlende UTF-8-Locale der Umgebung. Mit `LANG=C.UTF-8` kommt `Prüfung S06.vtt` an, die Header sind korrekt.
- **UX- und a11y-Review (T9)**, Befunde und Umsetzung:
  1. **Kontrast der Zeitstempel:** `base-content/60` erreicht auf Weiß nur 4,67:1 und auf der Hervorhebung des aktiven Segments etwa 4,3:1, also unter AA für kleine Schrift. Erhöht auf `/70`.
  2. **Zugänglicher Name:** Er lautete „0:00Hallo“, weil Angular das Leerzeichen zwischen Zeitstempel und Text entfernt. Behoben mit `&ngsp;`, ein Test prüft den Text.
  3. **Zeitstempel:** Sie sind jetzt `<time datetime="PT…S">`.
  4. **Ladezustand:** Das Skeleton hat `role="status"` mit sichtbar verstecktem Text statt eines `aria-label` ohne Inhalt.
  5. **Ohne Befund:** Tastatur (Tab erreicht jedes Segment, Enter springt), sichtbarer Fokusrahmen, Hervorhebung nicht nur über Farbe (zusätzlich fett), Zielgrößen ≥ 24 px, Labels in DE und EN.
- **Offen (DoD):** Der VLC-Test der SRT-Datei. VLC gibt es in der Entwicklungsumgebung nicht. Das Format entspricht den Snapshot-Tests, `1`, `00:00:00,000 --> …`, Leerzeile zwischen Cues.

## 6. Acceptance Criteria (DoD)
- [ ] Die exportierte SRT-Datei lässt sich in VLC fehlerfrei laden und ist synchron.
- [x] Der Player springt bei Klick auf ein Segment auf ±0,5 s genau.
