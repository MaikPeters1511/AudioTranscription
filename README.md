# 🎙️ Audio Transcription

Lokale, private Audio-Transkription auf Basis von [Whisper.net](https://github.com/sandrohanea/whisper.net) (whisper.cpp) — läuft komplett offline, ohne dass Audiodaten an einen externen Dienst gesendet werden.

Lade eine Audiodatei hoch, verfolge den Verarbeitungsstatus live über SignalR und erhalte das fertige Transkript direkt im Browser — mit optionaler Nachbearbeitung durch ein lokales Ollama-LLM.

## ✨ Features

- 🔒 **Vollständig offline** — Transkription läuft lokal via Whisper.net, keine Cloud-APIs
- 📤 **Drag & Drop Upload** — MP3, WAV, M4A, OGG (bis 10 MB, konfigurierbar)
- ⚡ **Live-Updates** — Job-Status wird per SignalR in Echtzeit an das Frontend gepusht
- 📄 **Transkript-Verwaltung** — Kopieren, als `.txt` herunterladen, Wort-/Zeichenanzahl
- 🌗 **Hell/Dunkel-Theme**, responsives UI (Desktop-Tabelle + Mobile-Karten)
- 🧠 **Optionale Nachbearbeitung** über Ollama (z. B. Zusammenfassung, Rechtschreibkorrektur)
- 🐳 **.NET Aspire** orchestriert API, Datenbank, Web-Frontend (und optional Ollama) für lokale Entwicklung

## 🏗️ Architektur

```
AudioTranscription.AppHost/          .NET Aspire orchestration (dev entrypoint)
AudioTranscription.Api/              ASP.NET Core Minimal API + SignalR Hub + Background Worker
AudioTranscription.Domain/           Entities, Enums (framework-unabhängig)
AudioTranscription.Infrastructure/   EF Core, Whisper.net-Integration, ffmpeg-Konvertierung
AudioTranscription.ServiceDefaults/  Gemeinsame Aspire-Service-Defaults (Telemetry, Health Checks)
AudioTranscription.Tests/            xUnit-Tests
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

Das Aspire-Dashboard zeigt dir die zugewiesenen Ports für API und Web-Frontend an. Beim ersten Start lädt Whisper.net automatisch das GGML-Modell (`base`) herunter.

### Optional: Ollama-Nachbearbeitung aktivieren

In `AudioTranscription.Api/appsettings.json`:

```json
"Features": { "OllamaPostProcessing": true }
```

Beim nächsten `dotnet run` startet Aspire zusätzlich einen Ollama-Container inkl. `llama3.2`-Modell.

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
dotnet test
```

## 📁 Konfiguration

Wichtige Einstellungen in `AudioTranscription.Api/appsettings.json`:

```json
{
  "Upload": {
    "MaxFileSizeBytes": 10485760,
    "AllowedContentTypes": ["audio/mpeg", "audio/wav", "audio/x-wav", "audio/mp4", "audio/x-m4a", "audio/ogg"],
    "TempStoragePath": "temp-uploads",
    "DeleteAfterTranscription": true
  },
  "Features": { "OllamaPostProcessing": false }
}
```

## 📡 API-Übersicht

| Methode | Endpunkt | Beschreibung |
|---|---|---|
| `POST` | `/api/audio-jobs` | Audiodatei hochladen, Transkriptions-Job anlegen |
| `GET` | `/api/audio-jobs` | Paginierte Liste aller Jobs |
| `GET` | `/api/audio-jobs/{id}` | Details & Transkript eines Jobs |
| `WS` | `/hubs/transcription` | SignalR-Hub für Live-Statusupdates (`JobCreated`, `JobStatusChanged`) |

## 🛠️ Tech-Stack

**Backend:** ASP.NET Core (.NET 10) · EF Core · SQL Server · SignalR · Whisper.net · ffmpeg · .NET Aspire
**Frontend:** Angular 22 (zoneless, Standalone Components, Signals) · Tailwind CSS 4 · DaisyUI 5

## 📄 Lizenz

Dieses Projekt steht unter der [MIT-Lizenz](LICENSE).
