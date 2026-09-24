# Story S14: Große Dateien und Video-Upload

> Epic: [E-01](./00-epic.openspec.md) · Phase 3 · Agenten: backend-agent, devops-agent, frontend-agent

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
  - [ ] Integrationstest: Eine Datei knapp unter dem Limit liefert `202`, knapp darüber `413`.
  - [ ] Speicherverbrauch der API bleibt bei einem 400-MB-Upload unter 200 MB.

### S14-T2 Video-Formate zulassen · backend · S
- `AllowedContentTypes` wird um `video/mp4`, `video/webm`, `video/x-matroska`, `video/quicktime` ergänzt, `MagicBytesValidator` um die zugehörigen Signaturen (`ftyp`, EBML).
- ffmpeg-Aufruf mit `-vn`, damit der Videostream ignoriert wird.
- **AC (TDD):**
  - [ ] Magic-Bytes-Tests pro Format.
  - [ ] Integrationstest mit einer kurzen MP4-Datei.

### S14-T3 Frontend: Upload-Fortschritt · frontend · S
- `HttpClient` mit `reportProgress: true` und `<progress>` mit ARIA-Werten.
- **AC:**
  - [ ] Unit-Test mit `HttpTestingController` und Progress-Events.

### S14-T4 Spike: Fortsetzbarer Upload (tus vs. eigener Chunked-Upload) · architect · S
- **AC:**
  - [ ] ADR in `docs/adr/` mit Empfehlung. Die Umsetzung wird eine eigene Folge-Story.

## 4. Acceptance Criteria (DoD)
- [ ] Eine 60-minütige MP3-Datei und ein 10-minütiges MP4-Video werden erfolgreich transkribiert.
