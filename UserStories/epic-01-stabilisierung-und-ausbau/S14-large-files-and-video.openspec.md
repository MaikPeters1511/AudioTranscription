# Story S14: Große Dateien und Video-Upload

> Epic: [E-01](./00-epic.openspec.md) · GitHub: #15 · Phase 3 · Agenten: backend-agent, devops-agent, frontend-agent

## 1. Description
Das Limit liegt bei 10 MB (`UploadOptions.MaxFileSizeBytes`), Kestrel erlaubt 15 MB (`Program.cs`). Das reicht nur für wenige Minuten MP3. Lange Meetings und Videos (MP4/MKV/WebM) sollen möglich werden. ffmpeg ist bereits im Einsatz und kann die Tonspur direkt extrahieren.

## 2. User Stories
- Als Nutzer möchte ich Aufnahmen von über einer Stunde hochladen.
- Als Nutzer möchte ich ein Video hochladen und das Transkript seiner Tonspur erhalten.
- Als Nutzer möchte ich bei großen Uploads einen Upload-Fortschritt sehen und einen abgebrochenen Upload fortsetzen können.

## 3. Tasks

### S14-T1 Limits zentral konfigurierbar und konsistent · backend + devops · S
- Kestrel `MaxRequestBodySize`, `FormOptions.MultipartBodyLengthLimit`, `UploadOptions.MaxFileSizeBytes` und nginx `client_max_body_size` (`AudioTranscription.Web/nginx.conf`) werden aus einem Wert abgeleitet. Standard z. B. 500 MB.
- Beim Upload wird per Streaming auf die Platte geschrieben (`MultipartReader`), nicht über `IFormFile` gepuffert.
- **AC:**
  - [x] Integrationstest: Eine Datei knapp unter dem Limit liefert `202`, knapp darüber `413`. (`UploadSizeLimitIntegrationTests`, 2 Tests, `LimitBytes = 1_000`)
  - [x] Speicherverbrauch der API bleibt bei einem 400-MB-Upload unter 200 MB. (`MultipartUploadParserTests.ParseAsync_With400MbBody_DoesNotGrowWorkingSetByMoreThan200Mb`, siehe Umsetzungsnotizen)

### S14-T2 Video-Formate zulassen · backend · S
- `AllowedContentTypes` wird um `video/mp4`, `video/webm`, `video/x-matroska`, `video/quicktime` ergänzt, `MagicBytesValidator` um die zugehörigen Signaturen (`ftyp`, EBML).
- ffmpeg-Aufruf mit `-vn`, damit der Videostream ignoriert wird.
- **AC (TDD):**
  - [x] Magic-Bytes-Tests pro Format. (`MagicBytesValidatorTests`, 4 neue Theory-Tests für `video/mp4`, `video/quicktime`, `video/webm`, `video/x-matroska`)
  - [x] Integrationstest mit einer kurzen MP4-Datei. (`UploadStorageIntegrationTests.Upload_WithAShortMp4Video_IsAccepted`)

### S14-T3 Frontend: Upload-Fortschritt · frontend · S
- `HttpClient` mit `reportProgress: true` und `<progress>` mit ARIA-Werten.
- **AC:**
  - [x] Unit-Test mit `HttpTestingController` und Progress-Events. (`audio-job.service.spec.ts`, neuer Block "large file upload progress (S14)", 2 Tests inkl. steigender Prozentwerte über mehrere `UploadProgress`-Events)

### S14-T4 Spike: Fortsetzbarer Upload (tus vs. eigener Chunked-Upload) · architect · S
- **AC:**
  - [x] ADR in `docs/adr/` mit Empfehlung. Die Umsetzung wird eine eigene Folge-Story. (`docs/adr/0006-resumable-upload-spike.md`, Empfehlung: tus-Protokoll)

## 4. Acceptance Criteria (DoD)
- [~] Eine 60-minütige MP3-Datei und ein 10-minütiges MP4-Video werden erfolgreich transkribiert. Die dafür relevanten technischen Voraussetzungen sind durch automatisierte Tests belegt (Streaming-Upload ohne Speicheraufblähung bis 400 MB, Format-Validierung für MP4/WebM/MKV/MOV, `-vn` im ffmpeg-Aufruf), eine echte 60-minütige Datei mit vollständigem Whisper-Durchlauf wurde in dieser Sandbox nicht end-to-end ausgeführt (kein Modell-Download möglich, siehe S08/S11/S13 zur selben Einschränkungsklasse) und ist manuell bzw. in einer Umgebung mit Internetzugang zu verifizieren.

## 5. Umsetzungsnotizen

- **`IFormFile` → `MultipartUploadParser` (S14-T1), empirisch begründet:** Die ursprüngliche Annahme, ASP.NET Core puffere große Multipart-Datei-Teile ohnehin nicht komplett im Arbeitsspeicher, wurde per Test widerlegt (CLAUDE.md „Beweise vor Behauptungen“): Ein erster Test mit dem bisherigen `IFormFile`-Binding zeigte bei einem 400-MB-Upload einen Anstieg des Working Sets von **~991 MB** – weit über dem 200-MB-Limit der DoD. Daraufhin wurde `AudioTranscription.Api/Uploads/MultipartUploadParser.cs` geschrieben, das den Datei-Teil direkt mit einem festen 80-KB-Puffer auf die Platte streamt (`MultipartReader`/`MultipartSection`), statt ihn über Model-Binding zu puffern. `AudioJobEndpoints.UploadAudioJob` wurde entsprechend umgebaut: Parameter `HttpRequest request` statt `IFormFile file, [FromForm] ...`; alle bestehenden 300 Backend-Tests liefen danach unverändert grün (keine Verhaltensänderung für bestehende Szenarien).
  - Der erste Versuch, das per Test zu verifizieren (`LargeUploadMemoryTests`, mittlerweile gelöscht), nutzte `WebApplicationFactory` mit echtem `HttpClient`. Dieser Test zeigte **auch nach der Umstellung** noch ~990 MB Anstieg – nicht weil der Server weiterhin puffert, sondern weil `TestServer`/`HttpClient`/`MultipartFormDataContent` im selben Prozess laufen und die In-Memory-Transport-Kette die gesamte Anfrage selbst puffert. Der Test maß also den Client, nicht den Server. Ersetzt durch `MultipartUploadParserTests.ParseAsync_With400MbBody_DoesNotGrowWorkingSetByMoreThan200Mb`, der `MultipartUploadParser.ParseAsync` direkt gegen einen synthetisch erzeugten, nie vollständig im Speicher gehaltenen Multipart-Body-Stream aufruft (`GeneratedMultipartBodyStream`) – dieser Test ist grün und misst tatsächlich das serverseitige Verhalten.
- **Validierungsreihenfolge in `UploadAudioJob` geändert:** Da `MultipartReader` die Teile in Wire-Reihenfolge liest (Datei typischerweise vor den Formularfeldern) und die Datei bereits beim Lesen auf die Platte geschrieben wird, wird die Größenprüfung (413) jetzt vor der Einstellungs-Validierung (Modell/Sprache) geprüft; bei jedem späteren Validierungsfehler (Content-Type, Magic-Bytes, Einstellungen) wird die bereits geschriebene Datei explizit gelöscht.
- **Video-Formate (S14-T2):** `video/mp4`/`video/quicktime` nutzen dieselbe `ftyp`-Box-Erkennung wie die bestehenden `audio/mp4`/`audio/x-m4a`-Signaturen (Offset 4); `video/webm`/`video/x-matroska` nutzen dieselbe EBML-Signatur (`0x1A 0x45 0xDF 0xA3`) wie `audio/webm`, da WebM und Matroska denselben Container-Header verwenden. `Audio16kHzWavConverter` bekam den zusätzlichen ffmpeg-Parameter `-vn`, damit nur die Tonspur extrahiert wird.
- **nginx/docker-compose-Ableitung (S14-T1, „aus einem Wert abgeleitet"):** `AudioTranscription.Web/nginx.conf` wurde durch `nginx.conf.template` ersetzt (nginx-Standard-Mechanismus: Dateien unter `/etc/nginx/templates/*.template` werden vom offiziellen `nginx:alpine`-Image beim Start automatisch per `envsubst` nach `/etc/nginx/conf.d/` gerendert). `docker-compose.yml` definiert einen einzigen Wert `UPLOAD_MAX_FILE_SIZE_MB` (Default 500), aus dem sowohl `Upload__MaxFileSizeBytes` (API, in Byte umgerechnet) als auch `NGINX_CLIENT_MAX_BODY_SIZE` (nginx, als `...M`) abgeleitet werden. Die Aspire-Variante (`AudioTranscription.AppHost`) betreibt das Frontend über den Angular-Dev-Server, nicht über nginx, und ist von dieser Änderung nicht betroffen.
- **Frontend-Client-Validierung nachgezogen (über die reine T3-AC hinaus):** Beim Review von `UploadComponent` fiel auf, dass die clientseitige Validierung noch das alte 10-MB-Limit und die alte, video-lose Format-Liste hatte (`maxSize`, `allowedTypes`, `isAllowedExtension`, `accept`-Attribut, i18n-Texte in `de.json`/`en.json`) – ohne diese Anpassung hätte das Frontend jeden Video-Upload und jede Datei über 10 MB bereits vor dem Request abgelehnt, unabhängig vom neuen Backend-Limit. Entsprechend aktualisiert und mit neuen Tests abgedeckt (`upload.component.spec.ts`: MP4-Upload akzeptiert, Ablehnung über 500 MB).
- **T4 (ADR 0006):** Empfehlung tus-Protokoll gegenüber eigenem Chunking-Schema, siehe `docs/adr/0006-resumable-upload-spike.md`. Keine Implementierung in dieser Story.
