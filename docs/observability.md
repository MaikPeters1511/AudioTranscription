# Observability (S29)

Die API liefert fachliche Metriken, Traces und Readiness-Checks. Alles bleibt lokal: Es gehen keine Daten an
externe Dienste, und weder Metriken noch Traces enthalten Dateinamen, Transkripttexte oder Fehlermeldungen
(nur Job-IDs und begrenzte Werte wie Modell, Modus und Ergebnis).

## Metriken (Meter `AudioTranscription`)

| Metrik | Art | Bedeutung | Tags |
|---|---|---|---|
| `transcription.jobs` | Zähler | beendete Jobs | `outcome` (`completed`, `failed`, `cancelled`), `model` |
| `transcription.duration` | Histogramm, s | Verarbeitungszeit eines Jobs | `outcome`, `model` |
| `transcription.realtime_factor` | Histogramm | Verarbeitungszeit geteilt durch Audiolänge (unter 1 = schneller als Echtzeit), nur für erfolgreiche Jobs | `model` |
| `transcription.queue.length` | Gauge | Jobs, die auf den Worker warten | - |
| `postprocessing.duration` | Histogramm, s | Dauer der LLM-Nachbearbeitung | `mode`, `outcome` |

Dazu kommen die Standardmetriken für ASP.NET Core, HttpClient und die .NET-Laufzeit.

**Histogramm-Grenzen:** Die Standardgrenzen von OpenTelemetry (0, 5, 10, 25, ...) lösen Werte um 1 nicht auf; p50 und p95 hätten für jeden kurzen Job immer 2,5 und 4,75 gezeigt. Die Histogramme haben deshalb eigene Grenzen: `transcription.duration` 0,25 s bis 2 h, `transcription.realtime_factor` 0,01 bis 20 (mit 1 als Grenze zwischen schneller und langsamer als Echtzeit) und `postprocessing.duration` 1 bis 600 s.

**Namen in Prometheus** (der Collector hängt Einheit und Art an): `transcription_jobs_total`, `transcription_duration_seconds_{bucket,sum,count}`, `transcription_realtime_factor_{bucket,sum,count}`, `transcription_queue_length`, `postprocessing_duration_seconds_{bucket,sum,count}`.

## Traces (Quelle `AudioTranscription`)

Ein Job erzeugt den Span `transcription.job` (Tags `job.id`, `job.model`, `job.outcome`) mit den Kindern
`transcription.convert` (ffmpeg), `transcription.whisper` und, falls angefordert, `transcription.diarize`.
Die Nachbearbeitung einer Fassung ist der Span `postprocessing` (Tag `postprocessing.mode`). Fehler setzen
den Status des Spans auf `Error`, ohne Meldung.

## Ansehen

- **Aspire-Dashboard (Entwicklung):** `dotnet run --project AudioTranscription.AppHost` zeigt Metriken und
  Traces unter *Metrics* und *Traces*, ohne weitere Einrichtung.
- **Prometheus und Grafana (docker compose):**

  ```bash
  echo "OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317" >> .env
  docker compose --profile observability up -d
  ```

  Grafana läuft unter <http://localhost:3000> (Benutzer `admin`, Passwort aus `GRAFANA_ADMIN_PASSWORD`, sonst
  `admin`; für alles außer lokale Tests ändern). Das Dashboard *Audio Transcription* ist vorbereitet
  (`docker/observability/grafana/dashboards/audio-transcription.json`). Prometheus ist unter
  <http://localhost:9090> erreichbar. Traces und Logs landen nur im Log des Collectors, ein Trace-Backend
  gehört nicht zum Profil.

## Health-Checks

`GET /health` (nur in der Development-Umgebung freigeschaltet, siehe ServiceDefaults) prüft:

| Check | Ergebnis |
|---|---|
| `database` | Healthy, wenn die Datenbank antwortet, sonst Unhealthy |
| `ffmpeg` | Healthy, wenn `ffmpeg` im `PATH` liegt, sonst Unhealthy |
| `whisper-model` | Healthy, wenn das Standardmodell heruntergeladen ist, sonst Degraded (der erste Job lädt es) |
| `ollama` | nur wenn konfiguriert; Degraded, wenn nicht erreichbar (die Transkription läuft trotzdem) |

Die Antwort enthält nur den Status, keine Pfade, Hosts oder Verbindungsdaten.

## Alarmschwellen (Empfehlung)

| Signal | Schwelle | Bedeutung |
|---|---|---|
| Fehlerquote `failed / alle` über 15 min | über 20 % | Modell, ffmpeg oder Eingaben prüfen |
| `transcription.queue.length` | über 10 für mehr als 30 min | Worker zu langsam oder blockiert |
| Echtzeitfaktor p95 | über 3 | Hardware zu schwach für das gewählte Modell (GPU oder kleineres Modell) |
| `postprocessing.duration` p95 | über 300 s | Ollama überlastet |

In Grafana lassen sich daraus Alert-Regeln ableiten; sie gehören bewusst nicht zum mitgelieferten Dashboard,
weil die richtigen Schwellen vom Betrieb abhängen.

## Geprüft mit echten Daten (S29-F1)

`docker compose --profile observability up` wurde gestartet; die API sendete an den Collector, Prometheus holte die Metriken, und jedes Panel
des Dashboards wurde über die Grafana-API mit einer echten Abfrage geprüft (Testjobs mit einem gesprochenen Satz und einer kaputten Datei,
Whisper `Tiny`). Ergebnis:

- Alle Metriknamen des Dashboards stimmen mit denen in Prometheus überein.
- Jobs pro Stunde, Fehlerquote, Wartende Jobs, Verarbeitungsdauer (p50/p95) und Echtzeitfaktor (p50/p95) zeigen Werte. Die Quantil-Panels brauchen mindestens zwei Messzeitpunkte mit neuen Jobs dazwischen (die API exportiert alle 60 Sekunden), sonst bleiben sie leer.
- Gefunden und behoben: die Standard-Histogrammgrenzen (siehe oben), und die Fehlerquote war leer, solange noch kein Job fehlgeschlagen war (jetzt 0 %).
- **Nicht geprüft:** das Panel *Nachbearbeitung* (`postprocessing_duration_seconds`), weil dafür ein Ollama-Modell laufen muss; sein Name folgt derselben Umwandlung wie die geprüften.
- Der Compose-Dienst `api` baute das CUDA-Image (kein `target`, Compose nimmt die letzte Stufe des Dockerfiles) und scheiterte; er baut jetzt die CPU-Stufe `final`. Zum Zeitpunkt der Messung ließ sich die Whisper-Bibliothek im Container nicht laden, die Messung lief deshalb mit der API außerhalb von Docker und dem Collector im Compose-Stack. Ursache waren zwei fehlende Pakete im Image (`libgomp1` für die Whisper-Bibliothek und `ffmpeg` für die Konvertierung); beide sind inzwischen im Dockerfile, und ein Upload im Compose-Stack wird auf arm64 transkribiert.
