# Story S01: Repo-Hygiene – keine Nutzerdaten und Build-Artefakte im Repository

> Epic: [E-01](./00-epic.openspec.md) · GitHub: #4 · Phase 1 · Agenten: devops-agent, sec-agent

## 1. Description
Unter `AudioTranscription.Api/temp-uploads/` sind zwei hochgeladene Audiodateien (`.mp3`, `.ogg`) eingecheckt. Für ein Produkt, das mit „privat & offline“ wirbt, ist das ein Datenschutzrisiko. Im Root fehlt eine `.gitignore`, es gibt nur `AudioTranscription.Web/.gitignore`. Dadurch landen `bin/`, `obj/`, `node_modules/` und die Whisper-Modelle (`whisper-models/`, mehrere hundert MB) schnell im Repo.

## 2. User Stories
- Als Betreiber möchte ich, dass keine hochgeladenen Audiodateien im Repository liegen, damit keine privaten Aufnahmen veröffentlicht werden.
- Als Entwickler möchte ich eine vollständige `.gitignore`, damit Build-Artefakte und Modelle nie versehentlich committet werden.

## 3. Tasks

### S01-T1 Root-`.gitignore` anlegen · devops · XS
- Basis: `dotnet new gitignore`, ergänzt um `temp-uploads/`, `**/whisper-models/`, `*.bin` (GGML), `node_modules/`, `.env`, `*.user`, `.vs/`, `.idea/`, Playwright-Reports.
- **AC:**
  - [ ] `git status` zeigt nach `dotnet build`, `npm ci` und einem Transkriptionslauf keine neuen, ungetrackten Dateien.
  - [ ] `temp-uploads/` und `whisper-models/` sind ignoriert.

### S01-T2 Eingecheckte Audiodateien aus dem Index entfernen · devops · XS
- `git rm --cached` bzw. `git rm` für beide Dateien in `AudioTranscription.Api/temp-uploads/`.
- **AC:**
  - [ ] `git ls-files | grep -Ei '\.(mp3|ogg|wav|m4a|webm)$'` liefert keine Treffer.

### S01-T3 Git-History bereinigen (Entscheidung D3) · devops + Repo-Owner · S
- Nur nach ausdrücklicher Freigabe durch den Owner: `git filter-repo --path AudioTranscription.Api/temp-uploads --invert-paths`, danach Force-Push und Hinweis an alle Klone.
- **AC:**
  - [x] Entscheidung ist dokumentiert (ADR oder Kommentar in dieser Story).
  - ~~Falls umgesetzt: `git log --all -- AudioTranscription.Api/temp-uploads` ist leer.~~
- **Entscheidung (2026-09-24, Repo-Owner):** Die History wird **nicht** bereinigt. Die zwei Dateien bleiben im Git-Verlauf, sind aber aus dem aktuellen Stand entfernt. Neue Uploads verhindern `.gitignore` und der CI-Check.

### S01-T4 Pre-Commit-Schutz gegen Audiodateien (optional) · devops · XS
- CI-Check (siehe S16), der fehlschlägt, wenn Audio- oder Modell-Dateien getrackt sind.
- **AC:**
  - [ ] Ein PR mit einer `.mp3`-Datei wird von der CI abgelehnt.

## 4. Acceptance Criteria (DoD)
- [ ] T1 und T2 sind gemergt, T3 ist entschieden (erledigt: nicht bereinigen).
- [ ] README weist darauf hin, dass Uploads nur lokal gespeichert werden.
