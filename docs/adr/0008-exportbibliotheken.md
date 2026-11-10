# ADR 0008: Bibliotheken für DOCX- und PDF-Export (S21, D8)

- Status: akzeptiert
- Datum: 2026-11-10

## Kontext
S21 erweitert den Export um DOCX, Markdown, JSON und PDF. Für DOCX und PDF braucht es Bibliotheken; die Lizenz muss zum Projekt passen (Entscheidung D8 des Epics E-02).

## Entscheidung
- **DOCX:** `DocumentFormat.OpenXml` (Open XML SDK), MIT-Lizenz, offizielles Microsoft-Paket, keine nativen Abhängigkeiten.
- **PDF:** `QuestPDF` unter der **Community-Lizenz** (kostenlos für Einzelpersonen, Open-Source-Projekte und Unternehmen unter 1 Mio. USD Jahresumsatz). Die Lizenz wird im Code explizit gesetzt (`PdfExporter`). Lato ist eingebettet, Umlaute funktionieren ohne Systemschriften.
- **Markdown/JSON:** eigene, abhängigkeitsfreie Exporter im Domain-Layer.
- Alle Formate implementieren `ITranscriptExporter` und rendern ein formatneutrales `ExportDocument`; weitere Formate sind durch eine zusätzliche Registrierung möglich.

## Konsequenzen
- Übersteigt der Betreiber die Umsatzgrenze von QuestPDF, ist eine kostenpflichtige Lizenz nötig oder `PdfExporter` wird gegen eine MIT-Alternative (z. B. PDFsharp) getauscht; nur diese Klasse ist betroffen.
- Das JSON-Schema ist über `schemaVersion` versioniert (aktuell 1); inkompatible Änderungen erhöhen die Version.
