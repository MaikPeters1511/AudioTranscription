# 🎙️ Audio Transcription

[![CI](https://github.com/MaikPeters1511/AudioTranscription/actions/workflows/ci.yml/badge.svg)](https://github.com/MaikPeters1511/AudioTranscription/actions/workflows/ci.yml)

Lokale, private Audio-Transkription auf Basis von [Whisper.net](https://github.com/sandrohanea/whisper.net) (whisper.cpp) — läuft komplett offline, ohne dass Audiodaten an einen externen Dienst gesendet werden.

Lade eine Audiodatei hoch, verfolge den Verarbeitungsstatus live über SignalR und erhalte das fertige Transkript direkt im Browser — mit optionaler Nachbearbeitung durch ein lokales Ollama-LLM.

## ✨ Features

- 🔒 **Vollständig offline** — Transkription läuft lokal via Whisper.net, keine Cloud-APIs
- 📤 **Drag & Drop Upload** — MP3, WAV, M4A, OGG (bis 10 MB, konfigurierbar)
- ⚡ **Live-Updates** — Job-Status und Fortschritt in Prozent werden per SignalR in Echtzeit an das Frontend gepusht
- 📄 **Transkript-Verwaltung** — Kopieren, als `.txt` herunterladen, Wort-/Zeichenanzahl
- 🌗 **Hell/Dunkel-Theme**, responsives UI (Desktop-Tabelle + Mobile-Karten)
- 🎛️ **Modell und Sprache wählbar** — pro Upload ein freigegebenes Whisper-Modell und die Sprache der Aufnahme (oder automatische Erkennung)
- ⏱️ **Zeitstempel und Untertitel** — Export als `.srt`/`.vtt`, Audio-Player mit mitlaufendem Transkript (Klick auf einen Satz springt dorthin)
- 🧠 **Optionale Nachbearbeitung** über Ollama (z. B. Zusammenfassung, Rechtschreibkorrektur)
- 🐳 **.NET Aspire** orchestriert API, Datenbank, Web-Frontend (und optional Ollama) für lokale Entwicklung

## 🔐 Datenschutz

- Hochgeladene Dateien werden nur lokal im Ordner `temp-uploads/` der API gespeichert (`Upload:TempStoragePath`) und standardmäßig nach erfolgreicher Transkription gelöscht (`Upload:DeleteAfterTranscription`).
- Der Audio-Player in der Detailansicht braucht die hochgeladene Datei. Mit der Standardeinstellung ist sie nach erfolgreicher Transkription gelöscht, dann zeigt die Ansicht nur die Segmente mit Zeitstempeln. Wer den Player nutzen will, setzt `Upload:DeleteAfterTranscription=false` und nimmt in Kauf, dass die Audiodateien auf dem Server bleiben.
- `temp-uploads/`, Audiodateien und die Whisper-Modelle sind per `.gitignore` ausgeschlossen. Der Job `Repo hygiene` im CI-Workflow lässt jeden PR fehlschlagen, der solche Dateien enthält.

## 🔑 Anmeldung

Die App ist nur nach Login nutzbar. Sie verwendet lokale Konten (ASP.NET Core Identity, [ADR 0003](docs/adr/0003-authentifizierung.md)), alle angemeldeten Benutzer sehen dieselben Jobs. Eine Selbst-Registrierung ist standardmäßig **aus**. Der erste Benutzer wird beim Start aus der Konfiguration angelegt, sofern noch keiner existiert:

| Umgebung | So setzt du den ersten Benutzer |
|---|---|
| Aspire | `dotnet user-secrets set "Parameters:initial-user-email" "du@example.com" --project AudioTranscription.AppHost` und ebenso `Parameters:initial-user-password`. Fehlen die Werte, fragt das Aspire-Dashboard nach. |
| docker compose | `.env.example` nach `.env` kopieren (wird nicht committet) und `INITIAL_USER_EMAIL`/`INITIAL_USER_PASSWORD` setzen |
| API direkt | Umgebungsvariablen `Auth__InitialUser__Email` und `Auth__InitialUser__Password` oder User-Secrets des API-Projekts |

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
    "MaxFileSizeBytes": 10485760,
    "AllowedContentTypes": ["audio/mpeg", "audio/wav", "audio/x-wav", "audio/mp4", "audio/x-m4a", "audio/ogg"],
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
  "Features": { "OllamaPostProcessing": false }
}
```

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

## 📡 API-Übersicht

Alle Endpunkte außer Login erfordern eine Anmeldung, sonst antworten sie mit `401`.

| Methode | Endpunkt | Beschreibung |
|---|---|---|
| `POST` | `/api/auth/login?useCookies=true` | Anmelden (`{ email, password }`), setzt das Session-Cookie |
| `POST` | `/api/auth/logout` | Abmelden |
| `GET` | `/api/auth/me` | Angemeldeter Benutzer (`{ email }`) |
| `POST` | `/api/audio-jobs` | Audiodatei hochladen (`file`, optional `model` und `language`), Transkriptions-Job anlegen. Unbekanntes Modell oder unbekannte Sprache: `400` |
| `GET` | `/api/transcription-options` | Wählbare Modelle, Standardmodell und Sprachen (`auto` ist immer möglich) |
| `GET` | `/api/audio-jobs` | Paginierte Liste aller Jobs |
| `GET` | `/api/audio-jobs/{id}` | Details & Transkript eines Jobs |
| `DELETE` | `/api/audio-jobs/{id}` | Job samt Transkript und Upload löschen (bricht einen laufenden Job vorher ab) |
| `POST` | `/api/audio-jobs/{id}/cancel` | Wartenden oder laufenden Job abbrechen |
| `POST` | `/api/audio-jobs/{id}/retry` | Fehlgeschlagenen oder abgebrochenen Job erneut einreihen |
| `GET` | `/api/audio-jobs/{id}/segments` | Zeitstempel-Segmente des Roh-Transkripts (`409`, solange der Job nicht abgeschlossen ist) |
| `GET` | `/api/audio-jobs/{id}/subtitles?format=srt\|vtt` | Untertitel-Download (`409` wie oben, `400` bei unbekanntem Format) |
| `GET` | `/api/audio-jobs/{id}/audio` | Hochgeladene Audiodatei mit HTTP-Range-Support; `410`, wenn sie schon gelöscht ist |
| `WS` | `/hubs/transcription` | SignalR-Hub für Live-Statusupdates (`JobCreated`, `JobStatusChanged`, `JobProgress`, `JobDeleted`) |

## 🛠️ Tech-Stack

**Backend:** ASP.NET Core (.NET 10) · EF Core · SQL Server · SignalR · Whisper.net · ffmpeg · .NET Aspire
**Frontend:** Angular 22 (zoneless, Standalone Components, Signals) · Tailwind CSS 4 · DaisyUI 5

## 📄 Lizenz

Dieses Projekt steht unter der [MIT-Lizenz](LICENSE).
