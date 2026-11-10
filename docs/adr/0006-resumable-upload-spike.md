# ADR 0006: Spike – Resumable Upload (tus vs. eigenes Chunking)

- **Status:** Akzeptiert. Spike aus S14; Entscheidung D9 für die Umsetzung in S27 siehe „Entscheidung für S27“ am Ende.
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

## Entscheidung für S27 (D9, 2026-11-10)

**tus** (`tusdotnet` 2.12.0 serverseitig, `tus-js-client` 4.x im Frontend), additiv zum bestehenden `POST /api/audio-jobs`.

Antworten auf die offenen Fragen des Spikes:

- **Ab welcher Größe tus?** Das Frontend nutzt tus ab 10 MiB (Konstante `RESUMABLE_UPLOAD_THRESHOLD_BYTES`), kleinere Dateien gehen weiter über den einfachen Multipart-Upload. Browser-Aufnahmen (S12) laufen über dieselbe Weiche und nehmen tus, sobald sie die Schwelle überschreiten. Der alte Endpoint bleibt bewusst erhalten (kleine Dateien, andere Clients, Skripte).
- **Aufräumen unvollständiger Uploads:** Jede tus-Sitzung wird in der Tabelle `UploadSessions` mit Besitzer und Zeitpunkt geführt. Ein Hintergrunddienst entfernt unvollständige Sitzungen samt Teildatei nach `Upload:IncompleteSessionRetentionHours` (Standard 24). Die tus-eigene Expiration wird nicht genutzt, weil sie nicht über `TimeProvider` testbar ist. Fertige Uploads wandern in den bestehenden Upload-Ordner und unterliegen von da an dem `OrphanedUploadCleanupService`.
- **Speicher:** Teil-Uploads liegen im Unterordner `tus/` von `Upload:TempStoragePath` (`TusDiskStore`), derselbe Docker-Volume wie die übrigen Uploads; ein eigener Provider ist nicht nötig. Beim Abschluss wird die Datei mit `File.Move` in `{jobId}{Endung}` verschoben, nicht kopiert.
- **Sicherheit:** Eine Sitzung gehört dem Benutzer, der sie angelegt hat; jeder tus-Request (`POST`, `HEAD`, `PATCH`, `DELETE`) verlangt Anmeldung und passenden Besitzer, fremde Sitzungen verhalten sich wie nicht vorhanden (404). Größe (`Upload:MaxFileSizeBytes`), Content-Type, Modell, Sprache, Diarisierung und Glossar werden beim Anlegen geprüft, die Magic Bytes an der fertig zusammengesetzten Datei. Ein Abschluss erzeugt genau einen Job; das Ergebnis (Job-ID oder Ablehnungsgrund) liefert `GET /api/uploads/{id}/job`.
- **Reverse-Proxy (offen, S27-T4):** Der Proxy muss `PATCH`, `HEAD`, `DELETE` und die `Upload-*`/`Tus-*`-Header durchlassen und Request-Buffering für `/api/uploads` abschalten; das Client-Chunk-Limit (5 MiB) liegt unter jedem üblichen `client_max_body_size`. Die Anpassung der mitgelieferten Konfiguration folgt in S27-T4.
- **Verbleibendes Risiko:** `tusdotnet` ist für `net6.0` gebaut und läuft auf .NET 10 unverändert; sollte das Projekt nicht mehr gepflegt werden, ist das Protokoll offen genug, um die Server-Seite zu ersetzen, ohne den Client zu ändern.
