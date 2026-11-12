# Changelog

Alle nennenswerten Änderungen an diesem Projekt werden hier dokumentiert.

Das Format orientiert sich an [Keep a Changelog](https://keepachangelog.com/de/1.1.0/). Da dieses Projekt bisher nicht versioniert/getaggt wird, sind die Einträge chronologisch nach Sprint/Story (siehe `UserStories/`) statt nach Versionsnummer gruppiert.

## [Unreleased]

### Hinzugefügt
- **PWA und Offline-Aufnahme (S33):** App auf dem Handy installierbar (Manifest, Service Worker nur für die App-Shell, nie für API-Antworten); Aufnahmen werden abschnittsweise in IndexedDB gesichert, überstehen Absturz und geschlossenes Tab und werden bei Verbindung automatisch hochgeladen (Liste „Wartet auf Upload“). Entscheidungen in ADR 0012.
- **Chat mit Transkripten / RAG (S24):** `POST /api/chat` beantwortet Fragen zu einer Aufnahme, einem Ordner oder allen Transkripten als Server-Sent-Events-Stream mit überprüfbaren Zitaten (Sprung in den Player), privaten Gesprächsverläufen, Rate-Limit und Anzeige, welches Modell antwortet und ob es ein externer Dienst ist. Transkripte gelten als untrusted Data (Schutz vor Prompt-Injection), Standard ist lokal über Ollama. Entscheidungen in ADR 0011.
- **Semantische und kombinierte Suche (S23):** Suche nach Bedeutung über Embeddings in Qdrant (lokal `bge-m3` über Ollama, OpenAI nur per ausdrücklicher Konfiguration), Modi „Wörtlich“, „Bedeutung“ und „Kombiniert“ (Reciprocal Rank Fusion), serverseitig erzwungene Berechtigungen, Admin-Neuaufbau des Index (`/api/admin/search/*`). Qualität auf einem deutschen Evaluationsset gemessen und dokumentiert. Entscheidungen in ADR 0010.
- **Protokoll-Vorlagen (S26):** Strukturierte Nachbearbeitung mit Vorlagen (Besprechungsprotokoll, Entscheidungsliste, Aufgaben mit Verantwortlichen), Ergebnis wird gegen ein JSON-Schema geprüft und als Markdown gerendert; eigene Vorlagen durch Admins.
- **Automatische Kapitel (S25):** Das lokale Sprachmodell gliedert lange Aufnahmen in Kapitel mit Titel und Kurzzusammenfassung; Marker auf der Zeitleiste, Kapitelliste mit Sprung per Klick oder Tastatur, Kapitel umbenennen und löschen.
- **Wiederaufnehmbarer Upload (S27):** Dateien ab 10 MiB werden per tus-Protokoll in Chunks hochgeladen (automatische Wiederholung, Pause/Fortsetzen, Aufräumen bei Abbruch); der bisherige Upload-Endpunkt bleibt unverändert.
- **Transkript-Editor (S32):** Segmente lassen sich im Player korrigieren, jede Änderung ist eine neue Version mit Verlauf, Diff und Wiederherstellen; das Rohtranskript bleibt unverändert, Suche, Untertitel und Exporte nutzen die korrigierte Fassung.
- **Batch-Upload und Warteschlange (S34):** Mehrere Dateien gleichzeitig hochladen (bis zu 3 parallel), Warteschlangen-Übersicht mit Position, geschätzter Wartezeit, Priorität und Abbrechen.
- **Glossare (S22):** Eigene Begriffslisten, die Whisper als Initial-Prompt für Namen und Fachwörter erhält; zu lange Listen werden gemeldet statt still abgeschnitten.
- **Weitere Exportformate (S21):** Transkript und Fassungen als Word (DOCX), Markdown, JSON und PDF, optional mit Zeitstempeln und Sprechernamen. Entscheidungen in ADR 0008.
- **Tags, Ordner und Favoriten (S20):** Persönliche Organisation der Jobs (auch geteilter), Filter in Liste und Suche, Verwaltungsseite „Organisieren“.
- **Observability (S29):** Fachliche Metriken (Jobs, Dauer, Echtzeitfaktor, Warteschlange), Traces der Pipeline und Readiness-Checks, optional mit Prometheus und Grafana; ohne Dateinamen oder Inhalte (siehe `docs/observability.md`).
- **Aufbewahrungsfristen (S30):** Optionales automatisches Löschen von Jobs (`Retention:TranscriptDays`) bzw. nur der Audiodatei (`Retention:AudioDays`), Schutz einzelner Jobs durch den Eigentümer, Lösch-Protokoll ohne Dateinamen oder Inhalte.
- **Jobs pro Benutzer und Freigabe (S19):** Jeder Benutzer sieht nur seine eigenen Jobs und die, die andere read-only mit ihm teilen (REST, SignalR und Suche serverseitig abgesichert). Entscheidungen in ADR 0007.
- **Benutzerverwaltung (S18):** Admin-Oberfläche für Benutzer und Rollen sowie „Passwort ändern“ für angemeldete Benutzer.
- **Automatisierung (S28):** API-Keys für Skripte (SHA-256-Hash, Scopes `jobs:read`/`jobs:write`, Ablauf, Rate-Limit), signierte Webhooks (`job.completed`/`job.failed`, HMAC-SHA256, Wiederholung mit Backoff, Zustellungsprotokoll, SSRF-Schutz), Ordner-Watch mit neustartsicherer Verarbeitung, Verwaltungsseiten „API-Keys“ und „Webhooks“ (DE/EN) und OpenAPI-Auth-Schemata. Entscheidungen in ADR 0009.
- **Initialer Admin-Account:** `InitialUserSeeder` legt beim ersten Start optional einen Admin-Benutzer aus der Konfiguration an (`Auth:InitialUser:Email`/`Password`), inkl. automatischer `Admin`-Rolle.
- **OpenAPI-Dokumentation (EN-3):** `GET /openapi/v1.json` liefert eine vollständige, maschinenlesbare Beschreibung aller Endpunkte.
- **GPU-Beschleunigung (S15):** Konfigurierbare Whisper-Runtime-Reihenfolge (`Whisper:RuntimeOrder`) mit automatischem CPU-Fallback; CUDA-fähiges Docker-Image (`--target final-cuda`) und `docker compose --profile gpu`.
- **Große Dateien & Video (S14):** Client-seitiges Upload-Limit angehoben, Video-Formate (MP4/WebM/MKV/MOV) werden akzeptiert — nur die Tonspur wird transkribiert.
- **Volltextsuche (S13):** SQL-Server-Full-Text-Search über Rohtranskripte, Segmente und Fassungen inkl. Deep-Link zur Fundstelle im Player.
- **Mikrofonaufnahme im Browser (S12):** Aufnahme-Reiter auf der Upload-Seite (`MediaRecorder`-API) mit Vorhören vor dem Transkribieren.
- **Sprechererkennung / Diarisierung (S11):** Offline-Sprecherzuordnung via sherpa-onnx, umbenennbare Sprecher, Übernahme in SRT/VTT-Export.
- **Transkript-Nachbearbeitung mit Ollama (S10):** Bereinigen, Zusammenfassen, Stichpunkte, Aufgaben, Übersetzen als zusätzliche Fassungen je Job.
- **Live-Fortschritt (S07):** Job-Fortschritt in Prozent wird per SignalR in Echtzeit gepusht.
- **Zeitstempel & Untertitel (S06):** Segmentierte Transkripte, SRT-/VTT-Export, Player mit mitlaufendem Transkript.
- **Modell- und Sprachauswahl (S08):** Whisper-Modell und Sprache sind pro Upload wählbar.
- **Job-Lifecycle (S09):** Jobs lassen sich abbrechen, erneut einreihen und löschen.
- **Anmeldung (S17):** Login über ASP.NET Core Identity (Cookie-Auth), alle Endpunkte außer Login erfordern eine Session.
- **CI-Pipeline (S16):** GitHub-Actions-Workflow mit Backend-/Frontend-Tests, Repo-Hygiene-Check und HTML-Testreports.

### Geändert
- **.NET 11 und Aspire 13.6.5:** Alle Projekte zielen auf `net11.0`; ASP.NET Core, EF Core und `Microsoft.Extensions.*` auf 11.0.0, Aspire-AppHost und -Pakete auf 13.6.5, Dockerfile-Basisimages und CI (`setup-dotnet`) auf 11.0.
- **Dokumentation:** README um neue Screenshots (Chat, Warteschlange, API-Keys, Webhooks) erweitert und die bestehenden aktualisiert.
- **Job-Wiederherstellung:** Offene Transkriptions-Jobs werden nach einem API-Neustart automatisch fortgesetzt.
- **SignalR-Reconnect:** Das Frontend verbindet sich nach Verbindungsabbrüchen automatisch neu und synchronisiert den Job-Status.
- **Roh- vs. bearbeitetes Transkript (S04):** Rohtranskript und nachbearbeitete Fassung werden getrennt gespeichert; das Frontend erlaubt das Umschalten zwischen Original und bearbeiteter Version.
- **EF-Core-Migrationen:** Werden beim API-Start automatisch angewendet (inkl. Baseline-Migration).
- **Upload-Konfiguration:** `Upload:DeleteAfterTranscription` und `Upload:OrphanedFileRetentionHours` steuern jetzt explizit, ob und wann hochgeladene Dateien nach der Transkription gelöscht werden.

### Behoben
- **Repo-Hygiene (S01):** CI lässt PRs fehlschlagen, die Audiodateien, Whisper-Modelle oder `temp-uploads/` versehentlich mit einchecken.
- **Web-Docker-Image:** Build- und Startfehler des Angular-Frontend-Images behoben.
- **Volltextsuche unter Docker:** `mssql-server`-APT-Repository wird vor der FTS-Installation im Dockerfile registriert, damit das Full-Text-Search-Paket zuverlässig installiert wird.

### Internationalisierung
- **DE/EN i18n (via Transloco):** Frontend-Texte sind vollständig über Übersetzungsdateien gepflegt, keine hartkodierten sichtbaren Strings.

### Tests
- **E2E-Kernflows mit gemockter API (S31):** Playwright-Specs (`*.mocked.spec.ts`) für Kernabläufe, Batch/Queue, Kapitel, Editor, tus-Upload und Offline-Aufnahme laufen nur mit dem Dev-Server und haben einen eigenen CI-Job.
- **Stabilere Tests:** Der Admin-Herabstufungs-Test ist unabhängig vom Timing, und die Upload-Tests halten den Transkriptions-Worker heraus.
- **Vitest-Unit-Tests (Web):** Frontend-Unit-Tests laufen über den Angular-Builder und sind in die CI integriert.
- **E2E-Test Job-Lifecycle (S09):** End-to-End-Test gegen den laufenden Stack.

## [0.1.0] – 2026-09-15
### Hinzugefügt
- Initialer Commit: Grundgerüst aus .NET-Aspire-AppHost, API, Domain, Infrastructure und Angular-Web-Projekt.

[Unreleased]: https://github.com/MaikPeters1511/AudioTranscription/compare/main...HEAD
