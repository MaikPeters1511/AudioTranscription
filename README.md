# 🎙️ Audio Transcription

[![CI](https://github.com/MaikPeters1511/AudioTranscription/actions/workflows/ci.yml/badge.svg)](https://github.com/MaikPeters1511/AudioTranscription/actions/workflows/ci.yml)

📝 [Changelog](CHANGELOG.md)

Lokale, private Audio-Transkription auf Basis von [Whisper.net](https://github.com/sandrohanea/whisper.net) (whisper.cpp) — läuft komplett offline, ohne dass Audiodaten an einen externen Dienst gesendet werden.

Lade eine Audiodatei hoch, verfolge den Verarbeitungsstatus live über SignalR und erhalte das fertige Transkript direkt im Browser — mit optionaler Nachbearbeitung durch ein lokales Ollama-LLM.

## ✨ Features

- 🔒 **Vollständig offline** — Transkription läuft lokal via Whisper.net, keine Cloud-APIs
- 📤 **Drag & Drop Upload** — MP3, WAV, M4A, OGG sowie Video (MP4, WebM, MKV, MOV — es wird nur die Tonspur verwendet), bis 500 MB (konfigurierbar), per Streaming direkt auf die Platte geschrieben
- 🎙️ **Direkt im Browser aufnehmen** — Mikrofonaufnahme ohne vorherigen Datei-Export, Vorhören vor dem Transkribieren (siehe unten, HTTPS erforderlich)
- ⚡ **Live-Updates** — Job-Status und Fortschritt in Prozent werden per SignalR in Echtzeit an das Frontend gepusht
- 📄 **Transkript-Verwaltung** — Kopieren, als `.txt` herunterladen, Wort-/Zeichenanzahl
- 🌗 **Hell/Dunkel-Theme**, responsives UI (Desktop-Tabelle + Mobile-Karten)
- 🎛️ **Modell und Sprache wählbar** — pro Upload ein freigegebenes Whisper-Modell und die Sprache der Aufnahme (oder automatische Erkennung)
- ⏱️ **Zeitstempel und Untertitel** — Export als `.srt`/`.vtt`, Audio-Player mit mitlaufendem Transkript (Klick auf einen Satz springt dorthin)
- 🧠 **Nachbearbeitung mit Ollama** — aus jedem fertigen Transkript per Klick eine bereinigte Fassung, Zusammenfassung, Stichpunkte, Aufgabenliste oder Übersetzung erzeugen
- 🗣️ **Sprechererkennung (optional)** — „Wer spricht wann?“: Segmente werden Sprechern zugeordnet, die sich umbenennen lassen; Export (SRT/VTT) enthält die Sprechernamen; läuft vollständig offline (sherpa-onnx)
- 🔍 **Volltextsuche** — alle Transkripte (Rohtext, Segmente, Fassungen) durchsuchen, inkl. Stemming für gebeugte deutsche Wortformen; Treffer springen direkt an die passende Stelle im Player
- 🐳 **.NET Aspire** orchestriert API, Datenbank, Web-Frontend (und optional Ollama) für lokale Entwicklung

## 🔐 Datenschutz

- Hochgeladene Dateien werden nur lokal im Ordner `temp-uploads/` der API gespeichert (`Upload:TempStoragePath`) und standardmäßig nach erfolgreicher Transkription gelöscht (`Upload:DeleteAfterTranscription`).
- Der Audio-Player in der Detailansicht braucht die hochgeladene Datei. Mit der Standardeinstellung ist sie nach erfolgreicher Transkription gelöscht, dann zeigt die Ansicht nur die Segmente mit Zeitstempeln. Wer den Player nutzen will, setzt `Upload:DeleteAfterTranscription=false` und nimmt in Kauf, dass die Audiodateien auf dem Server bleiben.
- `temp-uploads/`, Audiodateien und die Whisper-Modelle sind per `.gitignore` ausgeschlossen. Der Job `Repo hygiene` im CI-Workflow lässt jeden PR fehlschlagen, der solche Dateien enthält.

## 🔑 Anmeldung

Die App ist nur nach Login nutzbar. Sie verwendet lokale Konten (ASP.NET Core Identity, [ADR 0003](docs/adr/0003-authentifizierung.md)), alle angemeldeten Benutzer sehen dieselben Jobs. Eine Selbst-Registrierung ist standardmäßig **aus**.

### Initialer Admin-Account

Der `InitialUserSeeder` (`AudioTranscription.Api/Auth/InitialUserSeeder.cs`) legt beim Start automatisch den ersten Benutzer an — aber **nur**, solange noch kein User existiert (idempotent, läuft also bei jedem weiteren Start folgenlos durch) und **nur**, wenn Zugangsdaten konfiguriert sind. Aus Sicherheitsgründen gibt es bewusst **kein hardcodiertes Standardpasswort**: Fehlen E-Mail oder Passwort, loggt der Seeder lediglich eine Warnung und legt kein Konto an. Der angelegte Account erhält automatisch die `Admin`-Rolle (`RoleManager<IdentityRole>`).

Die Zugangsdaten werden ausschließlich aus der Konfiguration gelesen (`Auth:InitialUser:Email` / `Auth:InitialUser:Password`), je nach Umgebung wie folgt gesetzt:

| Umgebung | So setzt du den ersten Benutzer |
|---|---|
| Aspire | `dotnet user-secrets set "Parameters:initial-user-email" "du@example.com" --project AudioTranscription.AppHost` und ebenso `Parameters:initial-user-password`. Fehlen die Werte, fragt das Aspire-Dashboard nach. Im AppHost (`AudioTranscription.AppHost/Program.cs`) werden diese Parameter — das Passwort als `secret: true` — an die API als `Auth__InitialUser__Email`/`Auth__InitialUser__Password` durchgereicht. |
| docker compose | `.env.example` nach `.env` kopieren (wird nicht committet) und `INITIAL_USER_EMAIL`/`INITIAL_USER_PASSWORD` setzen |
| API direkt / lokale Entwicklung | User-Secrets des API-Projekts setzen: <br>`dotnet user-secrets set "Auth:InitialUser:Email" "admin@example.com" --project AudioTranscription.Api` <br>`dotnet user-secrets set "Auth:InitialUser:Password" "<dein-passwort>" --project AudioTranscription.Api` |
| Produktion / Container (ohne Aspire) | Umgebungsvariablen `Auth__InitialUser__Email` und `Auth__InitialUser__Password` setzen (Doppel-Unterstrich als Trenner, ASP.NET-Core-Konvention für verschachtelte Konfigurationswerte) |

- **Passwortregeln:** Es gelten die Standardregeln von Identity (mindestens 6 Zeichen, Groß- und Kleinbuchstabe, Ziffer, Sonderzeichen). Erfüllt das Passwort sie nicht, wird kein Konto angelegt, und das Log nennt den Grund.
- **Weitere Konten:** Über `Auth:AllowRegistration=true` lässt sich `POST /api/auth/register` vorübergehend freischalten.
- **Session-Cookie:** Es ist `HttpOnly`, `Secure` und `SameSite=Strict`. Außerhalb von `localhost` muss die App deshalb über **HTTPS** erreichbar sein.
- **CORS:** Frontend und API laufen same-origin (Dev-Proxy bzw. nginx), CORS ist deshalb standardmäßig zu. Andere Origins lassen sich über `Cors:AllowedOrigins` freigeben.

## 🏗️ Architektur

```
AudioTranscription.AppHost/          .NET Aspire orchestration (dev entrypoint)
AudioTranscription.Api/              ASP.NET Core Minimal API + SignalR Hub + Background Worker
AudioTranscription.Domain/           Entities, Enums (framework-unabhängig)
AudioTranscription.Infrastructure/   EF Core, Whisper.net-Integration, ffmpeg-Konvertierung
AudioTranscription.ServiceDefaults/  Gemeinsame Aspire-Service-Defaults (Telemetry, Health Checks)
Tests/AudioTranscription.Tests/      xUnit-Tests (Backend, siehe docs/adr/0001)
AudioTranscription.Web/              Angular 22 Frontend (Tailwind CSS + DaisyUI)
```

**Ablauf:** Datei-Upload → Validierung (Größe, MIME-Type, Magic Bytes) → Speicherung als Job (`Pending`) → Background Worker konvertiert Audio via `ffmpeg` zu 16 kHz-Mono-WAV → Whisper.net transkribiert → Ergebnis wird per SignalR an alle verbundenen Clients gepusht.

## 🔧 Voraussetzungen

| Tool | Version | Zweck |
|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0+ | API & Aspire AppHost |
| [Node.js](https://nodejs.org/) | 20+ | Angular-Frontend |
| [Docker](https://www.docker.com/) | aktuell | SQL Server (& optional Ollama) via Aspire |
| [ffmpeg](https://ffmpeg.org/) | aktuell, im `PATH` | Audiokonvertierung (macOS: `brew install ffmpeg`, Linux: `apt install ffmpeg`) |

## 🚀 Schnellstart (lokale Entwicklung mit Aspire)

.NET Aspire startet SQL Server (als Container), die API und das Angular-Frontend mit einem einzigen Befehl:

```bash
cd AudioTranscription.AppHost
dotnet run
```

Das Aspire-Dashboard zeigt dir die zugewiesenen Ports für API und Web-Frontend an. Whisper.net lädt jedes Modell beim ersten Einsatz automatisch herunter (siehe [Whisper-Modelle](#-whisper-modelle)).

### Optional: Ollama-Nachbearbeitung aktivieren

In `AudioTranscription.Api/appsettings.json`:

```json
"Features": { "OllamaPostProcessing": true }
```

Beim nächsten `dotnet run` startet Aspire zusätzlich einen Ollama-Container inkl. `llama3.2`-Modell.

In der Detailansicht eines abgeschlossenen Jobs erscheint dann eine Aktionsleiste „Weitere Fassungen“ mit einem Knopf je Modus (Bereinigen, Zusammenfassen, Stichpunkte, Aufgaben, Übersetzen). Ergebnisse erscheinen als zusätzliche Tabs neben „Original“; ein erneuter Klick auf denselben Modus ersetzt das Ergebnis. Lange Transkripte werden für Zusammenfassung und Aufgaben in Abschnitten verarbeitet und anschließend zusammengeführt (`PostProcessing:MaxChunkLength` in `appsettings.json`, Richtwert in Zeichen statt Tokens, da für das gewählte Ollama-Modell kein Tokenizer verfügbar ist).

### Optional: Sprechererkennung (Diarisierung) aktivieren

Sprechererkennung läuft vollständig offline über [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) und benötigt zwei ONNX-Modelle (pyannote-Segmentierung + Speaker-Embedding), die **nicht** mitgeliefert werden und selbst besorgt werden müssen (siehe [ADR 0004](docs/adr/0004-sprechererkennung.md)). In `AudioTranscription.Api/appsettings.json`:

```json
"Diarization": {
  "Enabled": true,
  "SegmentationModelPath": "/pfad/zu/segmentation.onnx",
  "EmbeddingModelPath": "/pfad/zu/embedding.onnx",
  "Threshold": 0.5,
  "NumThreads": 1
}
```

Beim Upload erscheint dann eine Checkbox „Sprechererkennung“. Ist sie aktiv, wird jedes Transkript-Segment nach der Transkription einem Sprecher zugeordnet (`Sprecher 1`, `Sprecher 2`, …); in der Detailansicht lassen sich Sprecher per Klick auf den Bearbeiten-Knopf umbenennen (z. B. „Anna“), SRT-/VTT-Export und die Segmentliste übernehmen den neuen Namen automatisch.

### Direkt im Browser aufnehmen

Die Upload-Seite hat zwei Reiter: „Datei hochladen“ und „Aufnehmen“. Im Aufnahme-Reiter starten/stoppen ein Knopf die Mikrofonaufnahme (`MediaRecorder`-API), danach lässt sich die Aufnahme vorhören und entweder transkribieren (wie ein normaler Upload) oder verwerfen. Das gewählte Modell/Sprache/Sprechererkennung (siehe oben) gilt auch für Aufnahmen.

**Wichtig:** `getUserMedia` (Mikrofonzugriff) verlangt einen [secure context](https://developer.mozilla.org/en-US/docs/Web/Security/Secure_Contexts) — die Seite muss über **HTTPS** oder `http://localhost` aufgerufen werden, sonst bietet der Browser den Reiter zwar an, aber die Berechtigungsanfrage schlägt fehl. In der Produktion (docker-compose/nginx) ist daher ein gültiges TLS-Zertifikat nötig, genau wie schon für das Session-Cookie (siehe [Anmeldung](#-anmeldung)).

Chrome und Firefox liefern die Aufnahme als `audio/webm;codecs=opus`, Safari als `audio/mp4` — beide werden vom Upload-Endpunkt akzeptiert. Manuell getestet wurde der volle Ablauf (Aufnehmen → Stoppen → Vorhören → Transkribieren) in Chromium; ein Test in echtem Firefox/Safari war in dieser Sandbox mangels installierter Browser nicht möglich (siehe Umsetzungsnotizen in der Story-Spezifikation).

### Volltextsuche

Die Suchseite (Reiter „Suche“) durchsucht Rohtranskripte, Segmente und erzeugte Fassungen (S10) per SQL Server Full-Text Search ([ADR 0005](docs/adr/0005-volltextsuche.md)) und springt bei einem Segment-Treffer direkt an die passende Stelle im Player.

**Wichtig:** Das Standard-Image `mcr.microsoft.com/mssql/server` bringt Full-Text Search **nicht** mit — es ist ein separates Paket. Aspire und `docker-compose.yml` bauen deshalb ein eigenes Image aus [`docker/mssql-fts/Dockerfile`](docker/mssql-fts/Dockerfile), das dieses Paket nachinstalliert; das Bauen braucht (einmalig) Internetzugriff auf `packages.microsoft.com`. Ohne dieses Image legt die Migration den Suchindex nicht an (sie schlägt nicht fehl, ist aber ein No-Op) und `GET /api/search` liefert einen Serverfehler.

## 🐳 Alternative: docker-compose

Für einen produktionsnäheren Stack ohne Aspire:

```bash
docker compose up --build
```

Startet SQL Server, API (Port `8080`) und das gebaute Angular-Frontend (Port `80`).

## 🖥️ Frontend separat starten

```bash
cd AudioTranscription.Web
npm install
npm start
```

Läuft standardmäßig auf `http://localhost:4200` und erwartet die API über einen Dev-Proxy unter `/api`.

## 🧪 Tests

```bash
# Backend (xUnit). Die Datenbank-Tests starten SQL Server per Testcontainers, Docker muss laufen.
dotnet test

# Frontend-Unit-Tests (Vitest über den Angular-Builder, Node 22.22.3+ oder 24)
cd AudioTranscription.Web
npm test -- --watch=false
```

Ablage der Tests: siehe [ADR 0001](docs/adr/0001-ablageort-von-tests.md).

## 📁 Konfiguration

Wichtige Einstellungen in `AudioTranscription.Api/appsettings.json`:

```json
{
  "Upload": {
    "MaxFileSizeBytes": 500000000,
    "AllowedContentTypes": [
      "audio/mpeg", "audio/wav", "audio/x-wav", "audio/mp4", "audio/x-m4a", "audio/ogg", "audio/webm",
      "video/mp4", "video/webm", "video/x-matroska", "video/quicktime"
    ],
    "TempStoragePath": "temp-uploads",
    "DeleteAfterTranscription": true,
    "OrphanedFileRetentionHours": 24
  },
  "Whisper": {
    "DefaultModel": "Base",
    "AllowedModels": ["Tiny", "Base", "Small", "Medium", "LargeV3"],
    "SupportedLanguages": ["de", "en", "fr", "..."],
    "ModelsDirectory": "whisper-models"
  },
  "Auth": { "AllowRegistration": false },
  "Cors": { "AllowedOrigins": [] },
  "PostProcessing": { "MaxChunkLength": 6000 },
  "Features": { "OllamaPostProcessing": false },
  "Diarization": {
    "Enabled": false,
    "SegmentationModelPath": "",
    "EmbeddingModelPath": "",
    "Threshold": 0.5,
    "NumThreads": 1
  }
}
```

`Upload:MaxFileSizeBytes` bestimmt außer der serverseitigen Prüfung auch die Kestrel- (`MaxRequestBodySize`) und Formular-Limits (`FormOptions.MultipartBodyLengthLimit`, siehe `Program.cs`) — es genügt, hier einen Wert zu ändern. Videos (MP4/WebM/MKV/MOV) werden akzeptiert, aber nur ihre Tonspur wird transkribiert (ffmpeg `-vn`). Beim docker-compose-Setup wird derselbe Wert zusätzlich für nginx' `client_max_body_size` gebraucht (sonst würde nginx große Uploads schon vor der API abweisen); dort steuert die Umgebungsvariable `UPLOAD_MAX_FILE_SIZE_MB` (Default 500) beide Seiten gemeinsam.

## 🎛️ Whisper-Modelle

Beim Upload wählt man ein Modell aus `Whisper:AllowedModels` (Standard: `Whisper:DefaultModel`) und die Sprache der Aufnahme aus `Whisper:SupportedLanguages` oder „Automatisch erkennen“. Eine vorgegebene Sprache verbessert die Erkennung, vor allem bei kurzen oder nicht-englischen Aufnahmen.

| Modell | Download | RAM (ca.) | Hinweis |
|---|---|---|---|
| `Tiny` | 75 MB | 0,3 GB | sehr schnell, ungenau |
| `Base` | 142 MB | 0,4 GB | Standard |
| `Small` | 466 MB | 0,9 GB | deutlich besser bei Deutsch |
| `Medium` | 1,5 GB | 2,1 GB | langsam ohne GPU |
| `LargeV3` | 2,9 GB | 3,9 GB | am genauesten, sehr langsam ohne GPU |

- Die Werte stammen aus der whisper.cpp-Dokumentation und sind Richtwerte.
- **Download:** Ein Modell wird beim ersten Job mit diesem Modell nach `Whisper:ModelsDirectory` geladen. Relative Pfade gelten ab dem Programmverzeichnis. Dieser Job dauert entsprechend länger.
- **Speicher:** Jedes einmal genutzte Modell bleibt bis zum Neustart geladen. Gibst du nur Modelle frei, die gemeinsam in den Arbeitsspeicher passen, kommt es nicht zu Engpässen.
- **Namen:** Erlaubt sind alle Namen von Whisper.nets `GgmlType`, z. B. `SmallEn` oder `LargeV3Turbo`.
- **Prüfung beim Start:** Eine ungültige Konfiguration verhindert den Start der API und nennt den Grund, z. B. ein unbekanntes Modell oder ein `DefaultModel`, das nicht in `AllowedModels` steht.

## 🚀 GPU-Beschleunigung

Whisper.net kann Modelle über CUDA (NVIDIA) oder CoreML (Apple Silicon) statt auf der CPU laufen lassen — besonders bei `Medium`/`LargeV3` ein deutlicher Geschwindigkeitsgewinn. Ohne passende GPU/Treiber fällt die App automatisch auf die CPU zurück; das ist keine Sonderbehandlung, sondern Whisper.nets eigene Standard-Ladereihenfolge (`Cuda → Cuda12 → Vulkan → CoreML → OpenVino → Cpu → CpuNoAvx`), die zuletzt immer die CPU versucht.

**Reihenfolge konfigurieren** (`Whisper:RuntimeOrder`, z. B. `["Cuda12", "Cuda", "Cpu"]`): schränkt ein, welche Runtimes in welcher Reihenfolge versucht werden, statt der eingebauten Liste. Wird beim Start geloggt (`Whisper native runtime order: [...]`); welche Runtime tatsächlich lädt, erscheint erst beim ersten genutzten Modell im Log (`Whisper native runtime in use: ...`), weil Modelle je Job lazy geladen werden.

**NVIDIA/CUDA (Docker):**
- Voraussetzungen auf dem Host: ein aktueller NVIDIA-Treiber und das [NVIDIA Container Toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/latest/install-guide.html) (stellt Docker den `nvidia`-Device-Treiber bereit).
- Das `-cuda`-Image (`docker build --target final-cuda ...`) basiert auf `nvidia/cuda:12.4.1-runtime-ubuntu22.04` statt dem normalen .NET-Runtime-Image, damit die von Whisper.net.Runtime.Cuda genutzten CUDA-Bibliotheken (`libcudart`, `libcublas`) vorhanden sind. Das Standard-Image bleibt CPU-only und dadurch klein — die CUDA-Pakete werden nur für dieses Ziel gebaut (`IncludeCudaRuntime=true`).
- Start: `docker compose --profile gpu up api-gpu` (läuft neben dem normalen `api`-Dienst auf Port 8081, statt ihn zu ersetzen). `nvidia-smi` im Container (`docker compose exec api-gpu nvidia-smi`) zeigt die GPU-Auslastung während einer Transkription.
- Welche CUDA-Version zum Treiber passt, hängt von der Whisper.net.Runtime.Cuda-Version ab; im Zweifel die vom NVIDIA Container Toolkit installierte CUDA-Version prüfen.

**Apple Silicon/CoreML:** Läuft nativ (kein Docker) auf macOS mit Apple Silicon; die CoreML-Runtime ist bereits Teil des Standard-Pakets. Erfordert eine unterstützte macOS-Version für Whisper.net.Runtime.CoreML (siehe dessen Release-Hinweise) — auf Linux/Windows wird diese Runtime automatisch übersprungen.

**Bekannte Einschränkung:** In dieser Entwicklungsumgebung ohne GPU-Hardware und ohne laufenden Docker-Daemon konnte weder ein echter CPU-vs-GPU-Benchmark noch der `-cuda`-Image-Build durchgeführt werden (siehe Story S15 in `UserStories/`); der CPU-Fallback selbst wurde aber per Test verifiziert (`WhisperRuntimeConfiguratorTests`).

## 📡 API-Übersicht

Alle Endpunkte außer Login erfordern eine Anmeldung, sonst antworten sie mit `401`.

**OpenAPI-Spezifikation:** `GET /openapi/v1.json` liefert die vollständige, maschinenlesbare Beschreibung aller Endpunkte (ASP.NET Cores eingebaute `AddOpenApi`/`MapOpenApi`-Unterstützung), ebenfalls hinter der Standard-Auth-Policy.

| Methode | Endpunkt | Beschreibung |
|---|---|---|
| `POST` | `/api/auth/login?useCookies=true` | Anmelden (`{ email, password }`), setzt das Session-Cookie |
| `POST` | `/api/auth/logout` | Abmelden |
| `GET` | `/api/auth/me` | Angemeldeter Benutzer (`{ email }`) |
| `POST` | `/api/audio-jobs` | Audiodatei hochladen (`file`, optional `model`, `language` und `diarize`), Transkriptions-Job anlegen. Unbekanntes Modell/Sprache oder `diarize=true` ohne konfigurierte Diarisierung: `400` |
| `GET` | `/api/transcription-options` | Wählbare Modelle, Standardmodell, Sprachen (`auto` ist immer möglich), `postProcessingEnabled`, `diarizationEnabled` |
| `GET` | `/api/audio-jobs` | Paginierte Liste aller Jobs |
| `GET` | `/api/audio-jobs/{id}` | Details & Transkript eines Jobs |
| `DELETE` | `/api/audio-jobs/{id}` | Job samt Transkript und Upload löschen (bricht einen laufenden Job vorher ab) |
| `POST` | `/api/audio-jobs/{id}/cancel` | Wartenden oder laufenden Job abbrechen |
| `POST` | `/api/audio-jobs/{id}/retry` | Fehlgeschlagenen oder abgebrochenen Job erneut einreihen |
| `GET` | `/api/audio-jobs/{id}/segments` | Zeitstempel-Segmente des Roh-Transkripts, bei Diarisierung inkl. `speakerIndex`/`speakerName` (`409`, solange der Job nicht abgeschlossen ist) |
| `GET` | `/api/audio-jobs/{id}/subtitles?format=srt\|vtt` | Untertitel-Download, Cues bei Diarisierung mit Sprechername (`409` wie oben, `400` bei unbekanntem Format) |
| `GET` | `/api/audio-jobs/{id}/speakers` | Erkannte Sprecher eines Jobs mit aufgelöstem Namen (`Sprecher N` bis umbenannt) |
| `PUT` | `/api/audio-jobs/{id}/speakers/{index}` | Sprecher umbenennen (`{ displayName }`, max. 100 Zeichen); `404` bei unbekanntem Job/Sprecher |
| `GET` | `/api/audio-jobs/{id}/audio` | Hochgeladene Audiodatei mit HTTP-Range-Support; `410`, wenn sie schon gelöscht ist |
| `POST` | `/api/audio-jobs/{id}/variants` | Fassung erzeugen/neu erzeugen (`{ mode, targetLanguage? }`); `409` außer bei `Completed`, `503` ohne konfiguriertes Ollama |
| `GET` | `/api/audio-jobs/{id}/variants` | Erzeugte Fassungen eines Jobs |
| `GET` | `/api/search?q=&page=&pageSize=` | Volltextsuche über Rohtranskripte, Segmente und Fassungen; Treffer mit Snippet, Hervorhebungs-Offsets und optional `segmentStartMs`; `400` ohne `q` |
| `WS` | `/hubs/transcription` | SignalR-Hub für Live-Statusupdates (`JobCreated`, `JobStatusChanged`, `JobProgress`, `VariantCompleted`, `JobDeleted`) |

## 🛠️ Tech-Stack

**Backend:** ASP.NET Core (.NET 10) · EF Core · SQL Server · SignalR · Whisper.net · ffmpeg · .NET Aspire
**Frontend:** Angular 22 (zoneless, Standalone Components, Signals) · Tailwind CSS 4 · DaisyUI 5

## 📝 Changelog

Nennenswerte Änderungen werden in [CHANGELOG.md](CHANGELOG.md) festgehalten.

## 📄 Lizenz

Dieses Projekt steht unter der [MIT-Lizenz](LICENSE).
