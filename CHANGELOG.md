# Changelog

Alle nennenswerten Änderungen an diesem Projekt werden hier dokumentiert.

Das Format orientiert sich an [Keep a Changelog](https://keepachangelog.com/de/1.1.0/). Da dieses Projekt bisher nicht versioniert/getaggt wird, sind die Einträge chronologisch nach Sprint/Story (siehe `UserStories/`) statt nach Versionsnummer gruppiert.

## [Unreleased]

### Hinzugefügt
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
- **Vitest-Unit-Tests (Web):** Frontend-Unit-Tests laufen über den Angular-Builder und sind in die CI integriert.
- **E2E-Test Job-Lifecycle (S09):** End-to-End-Test gegen den laufenden Stack.

## [0.1.0] – 2026-09-15
### Hinzugefügt
- Initialer Commit: Grundgerüst aus .NET-Aspire-AppHost, API, Domain, Infrastructure und Angular-Web-Projekt.

[Unreleased]: https://github.com/MaikPeters1511/AudioTranscription/compare/main...HEAD
