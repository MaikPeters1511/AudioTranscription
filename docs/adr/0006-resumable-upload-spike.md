# ADR 0006: Spike – Resumable Upload (tus vs. eigenes Chunking)

- **Status:** Akzeptiert (Spike; keine Implementierung in dieser Story)
- **Datum:** 2026-09-24
- **Kontext-Referenz:** Epic E-01, Story S14 (#15), Task S14-T4 (#75)

## Kontext

S14 hebt die Upload-Obergrenze auf 500 MB und erlaubt Video-Uploads. Bei sehr großen Dateien und mobilen/instabilen Verbindungen bricht ein einzelner HTTP-`POST`-Request bei einem Verbindungsabbruch komplett ab; die Nutzerin muss den kompletten Upload neu starten. S14-T4 verlangt ausdrücklich nur einen **Spike** (Technologie-Vergleich mit Empfehlung), keine Implementierung – die Umsetzung ist laut Task-Beschreibung eine eigene Folge-Story.

Zwei Ansätze wurden verglichen:

1. **tus-Protokoll** (offener Standard, [tus.io](https://tus.io)), server- und clientseitig über etablierte Bibliotheken (`tusdotnet` für .NET, `tus-js-client` für Angular).
2. **Eigenes Chunked-Upload-Schema**: Client zerlegt die Datei in Chunks, sendet sie sequenziell an einen eigenen Endpunkt (`PATCH /api/audio-jobs/{id}/upload-chunks/{index}`), Server hängt sie an eine Datei auf der Platte an; ein `HEAD`-artiger Endpunkt liefert den zuletzt empfangenen Chunk-Index für den Fortsetzungsfall.

## Bewertung

| Kriterium | tus-Protokoll | Eigenes Chunking |
|---|---|---|
| Implementierungsaufwand | Gering: `tusdotnet` als Middleware registrieren, Storage-Provider (Dateisystem) konfigurieren; Client über `tus-js-client`, der Resume/Retry/Chunking bereits kapselt | Hoch: eigenes Wire-Format, eigene Resume-Logik (Offset-Aushandlung), eigene Fehlerbehandlung bei doppelten/fehlenden Chunks, eigene Client-Logik für Retry mit exponentiellem Backoff |
| Standardkonformität | Ja – offener, versionierter Protokollstandard, von mehreren großen Anbietern (Vimeo, Cloudflare) produktiv eingesetzt | Nein – Ad-hoc-Format, nur von diesem Projekt verstanden, keine Interoperabilität mit Fremdclients |
| Wartbarkeit | Bibliothek wird extern gepflegt (Sicherheitsfixes, Edge Cases) | Jeder Edge Case (Netzwerkabbruch mitten im Chunk, parallele Uploads derselben Datei, Aufräumen verwaister Teil-Uploads) muss selbst bedacht und getestet werden |
| Integration mit `MultipartUploadParser` (S14-T1) | Getrennter Pfad: tus nutzt eigene Endpunkte und ein eigenes Storage-Format (Datei + `.info`-Metadatendatei), die erst nach vollständigem Upload in die bestehende `AudioJob`-Pipeline überführt werden müssten | Ebenfalls ein separater Endpunkt nötig; geringfügig einfacher an das bestehende `ITempFileStore`-Muster anzulehnen, da Bytes ohnehin sequenziell an eine Datei angehängt werden |
| Frontend-Aufwand | `tus-js-client` übernimmt Chunking, Fortschritt, automatischen Resume nach Verbindungsabbruch (`fingerprint`-basiert, übersteht sogar einen Seiten-Reload) | Müsste in `AudioJobService` von Hand nachgebaut werden: Chunk-Iteration, Fortschrittsberechnung, Resume-Erkennung nach Reload (z. B. über `localStorage`) |
| Risiko für den bestehenden Upload-Pfad | Additiv – der bestehende `POST /api/audio-jobs` (S14-T1) bleibt für kleinere Dateien/Aufnahmen unverändert; tus wäre ein zusätzlicher, opt-in Pfad für große Dateien | Gleiches additive Modell möglich, aber mit deutlich mehr selbst zu wartendem Code für denselben Nutzen |

## Entscheidung

**Empfehlung: tus-Protokoll**, umgesetzt in einer eigenen Folge-Story (kein Bestandteil von S14).

Begründung:
- Resumable Upload ist ein gelöstes Problem mit einem offenen, weit verbreiteten Standard; ein eigenes Chunking-Schema würde denselben Funktionsumfang mit deutlich mehr selbst zu wartendem Code (Server *und* Client) nachbauen, ohne einen erkennbaren Vorteil gegenüber tus zu bieten.
- `tusdotnet` und `tus-js-client` sind aktiv gepflegt und decken die schwierigen Teile (Offset-Aushandlung, Fingerprinting für Resume nach Seiten-Reload, parallele Chunk-Uploads) bereits ab.
- Der bestehende Upload-Pfad (`MultipartUploadParser`, S14-T1) bleibt unverändert bestehen; tus würde als zusätzlicher, opt-in Endpunkt für große Dateien ergänzt, ohne bestehende Uploads (Browser-Aufnahme aus S12, kleine Dateien) zu beeinflussen.

## Konsequenzen

- **Kein Code in dieser Story:** S14-T4 ist als Spike abgegrenzt; die tatsächliche tus-Integration (Server-Middleware, Frontend-Client, Übergang vom tus-Upload-Objekt zum bestehenden `AudioJob`) ist eine eigene, noch zu schreibende Folge-Story.
- **Migration bestehender Uploads nicht nötig:** Der aktuelle `POST /api/audio-jobs`-Endpunkt (S14-T1) bleibt der Standardweg für alle Uploads unterhalb einer noch festzulegenden Schwelle (z. B. Browser-Aufnahmen aus S12, die ohnehin clientseitig vollständig vorliegen); tus ergänzt ihn nur für sehr große Datei-Uploads.
- **Offene Fragen für die Folge-Story:** ab welcher Dateigröße tus statt des einfachen Uploads angeboten wird; wie unvollständige tus-Uploads nach Ablauf einer Frist aufgeräumt werden (analog zu `OrphanedFileRetentionHours` in `UploadOptions`); ob `tusdotnet`s Dateisystem-Storage-Provider für die docker-compose-Bereitstellung ausreicht oder ein eigener Provider nötig ist.
