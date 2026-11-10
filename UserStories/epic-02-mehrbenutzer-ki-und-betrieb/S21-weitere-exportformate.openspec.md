# Story S21: Weitere Exportformate

> Epic: [E-02](./00-epic.openspec.md) · Phase 2 · Agenten: backend-agent, frontend-agent · Größe: M · Entscheidung D8

## 1. Description
Exportiert werden bisher `.txt`, `.srt` und `.vtt`. Für die Weiterverarbeitung kommen DOCX, Markdown, JSON und PDF hinzu, jeweils mit Sprechernamen und Zeitstempeln.

## 2. User Stories
- Als Benutzer möchte ich ein Transkript als Word-Dokument herunterladen, damit ich es weiterbearbeiten kann.
- Als Entwickler möchte ich ein strukturiertes JSON, damit ich Transkripte maschinell weiterverarbeite.

## 3. API & Backend Requirements
- `GET /api/jobs/{id}/export?format=docx|md|json|pdf&variant=&timestamps=&speakers=`
- Formatierer hinter einem gemeinsamen Interface (`ITranscriptExporter`), erweiterbar wie die bestehenden SRT/VTT-Writer.
- JSON-Schema stabil und versioniert (`schemaVersion`).

## 4. Tasks

### S21-T1 Entscheidung zu Bibliotheken (D8) · architect · XS
- **AC:** Lizenzprüfung dokumentiert (kurzer ADR-Absatz oder Notiz im Epic).

### S21-T2 Markdown und JSON · backend · S
- **AC (TDD):** Golden-File-Tests, Sprecher und Zeitstempel optional, Sonderzeichen korrekt escaped.

### S21-T3 DOCX · backend · M
- **AC (TDD):** Datei öffnet in Word/LibreOffice, Überschriften je Sprecherwechsel, Test liest die erzeugte Datei per Open XML zurück.

### S21-T4 PDF · backend · M
- **AC (TDD):** Seitenumbruch, Kopfzeile mit Dateiname und Datum, Sonderzeichen/Umlaute korrekt.

### S21-T5 Frontend: Export-Menü · frontend · S
- **AC:** Dropdown mit Format und Optionen, `Content-Disposition`-Dateiname ohne Pfadanteile, DE/EN, Tastatur und ARIA.

## 5. Acceptance Criteria (DoD)
- [ ] Alle Formate sind für Roh- und nachbearbeitete Fassungen verfügbar.
- [ ] Autorisierung aus S19 gilt, Swagger dokumentiert den Endpoint.

## 6. Umsetzung
- Endpoint: `GET /api/audio-jobs/{id}/export` (Pfad entsprechend der bestehenden Routen statt `/api/jobs`), `variant` = Id der fertigen Fassung.
- Bibliotheken laut [ADR 0008](../../docs/adr/0008-exportbibliotheken.md): Open XML SDK (DOCX), QuestPDF Community (PDF).
- `ITranscriptExporter` und `ExportDocument` im Domain-Layer, DOCX/PDF in Infrastructure.
- Frontend: `app-job-export` (Format, Zeitstempel, Sprecher) in der Detailansicht.
