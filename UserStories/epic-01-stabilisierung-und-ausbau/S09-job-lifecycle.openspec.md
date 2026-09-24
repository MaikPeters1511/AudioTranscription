# Story S09: Jobs löschen, abbrechen und neu starten

> Epic: [E-01](./00-epic.openspec.md) · GitHub: #10 · Phase 2 · Agenten: backend-agent, frontend-agent, sec-agent, ux-agent

## 1. Description
Die API bietet nur `POST /api/audio-jobs` und `GET`. Nutzer können Transkripte nicht löschen, was wegen DSGVO (Recht auf Löschung) und Privatsphäre nötig ist. Hängende oder fehlerhafte Jobs lassen sich weder abbrechen noch neu starten.

## 2. User Stories
- Als Nutzer möchte ich einen Job samt Transkript und Audiodatei endgültig löschen, damit meine Daten nicht dauerhaft gespeichert bleiben.
- Als Nutzer möchte ich einen laufenden Job abbrechen, damit ich eine versehentlich hochgeladene lange Datei nicht abwarten muss.
- Als Nutzer möchte ich einen fehlgeschlagenen Job neu starten, damit ich die Datei nicht erneut hochladen muss.

## 3. API & Backend Requirements
- **Auth (D4):** Erledigt durch S17 (#94). Alle Endpoints verlangen eine Anmeldung, die Jobs sind gemeinsam für alle Angemeldeten.
- **Endpoints:**
  - `DELETE /api/audio-jobs/{id}` → `204`; `404`, wenn der Job unbekannt ist.
  - `POST /api/audio-jobs/{id}/cancel` → `202`; `409`, wenn der Job nicht `Pending` oder `Processing` ist.
  - `POST /api/audio-jobs/{id}/retry` → `202`; `409`, wenn der Job nicht `Failed` oder `Cancelled` ist; `410 Gone`, wenn die Quelldatei fehlt.
- **Enum:** `AudioJobStatus.Cancelled = 4`.
- **SignalR:** Neue Events `JobDeleted(id)`; `JobStatusChanged` deckt auch `Cancelled` ab.

## 4. Tasks

### S09-T1 `Cancelled`-Status und Abbruch-Registry · backend · S
- Singleton `JobCancellationRegistry` (`ConcurrentDictionary<Guid, CancellationTokenSource>`). Der Worker registriert pro Job einen verknüpften CTS mit dem `stoppingToken`.
- **AC (TDD):**
  - [ ] Test: `Cancel(id)` löst den Token des laufenden Jobs aus.
  - [ ] Test: Der Worker setzt bei Abbruch `Cancelled` statt `Failed` und löscht die Temp-Datei (S03).

### S09-T2 `POST /cancel` · backend · S
- `Pending` → direkt `Cancelled`, der Worker überspringt den Job (siehe S02-T3). `Processing` → Token auslösen.
- **AC:**
  - [ ] Integrationstests für `202`, `404` und `409`.

### S09-T3 `DELETE` · backend · S
- Laufender Job wird erst abgebrochen, dann werden DB-Eintrag, abhängige Daten (Segmente aus S06, Varianten aus S10) und die Datei gelöscht. SignalR sendet `JobDeleted`.
- **AC:**
  - [ ] Integrationstest: Nach `DELETE` liefert `GET` `404` und die Datei existiert nicht mehr.
  - [ ] Das Löschen wird nur mit der Job-ID geloggt, ohne Dateinamen (Datenschutz).

### S09-T4 `POST /retry` · backend · S
- Voraussetzung: Die Quelldatei ist noch vorhanden (D2). Fehlerfelder werden zurückgesetzt, Status → `Pending`, der Job wird eingereiht.
- **AC:**
  - [ ] Integrationstests für `202`, `409` und `410`.

### S09-T5 Frontend: Aktionen in Liste und Detailansicht · frontend · M
- Buttons „Abbrechen“, „Neu starten“ und „Löschen“, je nach Status sichtbar. Löschen mit DaisyUI-`modal` als Bestätigungsdialog (Fokus-Falle, `Esc` schließt, Fokus kehrt zum Auslöser zurück). Der Service reagiert auf das `JobDeleted`-Event. Alle Texte kommen aus i18n.
- **AC:**
  - [ ] Unit-Tests für die Sichtbarkeit je Status.
  - [ ] Nach dem Löschen im Detail erfolgt die Navigation zur Liste.

### S09-T6 E2E-Test des Lebenszyklus · qa · S
- Playwright: Upload → Abbrechen → Neu starten → Löschen.
- **AC:**
  - [ ] Test ist grün (lokal, später in S16-T5).

## 5. Umsetzungsnotizen (2026-09-24)
- **D2 (Repo-Owner):** Die Upload-Datei wird nur nach **erfolgreicher** Transkription gelöscht, oder wenn der Job nicht mehr existiert. Bei `Failed` und `Cancelled` bleibt sie für einen Neustart liegen. Entfernt wird sie durch `DELETE`, einen erfolgreichen Retry oder das Aufräumen verwaister Dateien nach `OrphanedFileRetentionHours`. Das ersetzt die Löschregel aus S03-T1.
- **T1 abweichend von der ursprünglichen AC:** Bei Abbruch wird die Datei wegen D2 **behalten**, nicht gelöscht.
- **Abbruch von `Processing`:** Läuft der Job gerade im Worker, wird er über `JobCancellationRegistry` abgebrochen. Ein veralteter `Processing`-Job ohne laufenden Worker wird direkt auf `Cancelled` gesetzt.
- **Löschen eines laufenden Jobs:** Der Worker bemerkt die Löschung (`DbUpdateConcurrencyException`) und endet ohne Fehler.
- **Frontend:**
  - `JobActionsComponent` mit nativem `<dialog>` (Fokus-Falle, `Esc`, Rückkehr des Fokus).
  - Kompakte Icon-Buttons mit ARIA-Label inklusive Dateiname in der Desktop-Tabelle. Die mobilen Karten sind Links, dort gibt es die Aktionen in der Detailansicht.
  - Nach Abbrechen oder Neu starten lädt der Service den Job neu, unabhängig von SignalR.
  - Löschungen meldet der Service über `jobDeleted$`, die Detailansicht wechselt dann zur Liste. Das gilt auch, wenn ein anderer Client löscht.
- **T6:** `AudioTranscription.Web/tests/job-lifecycle.spec.ts` läuft gegen den echten Stack, Zugangsdaten per `E2E_EMAIL`/`E2E_PASSWORD`. Getestet werden Upload, `Failed`, Neu starten, der Löschen-Dialog mit `Esc` und die Bestätigung. Der Abbruch eines laufenden Jobs lässt sich ohne Whisper-Modell nicht deterministisch E2E testen, er ist per Integrations- und Unit-Tests abgedeckt.

## 6. Acceptance Criteria (DoD)
- [ ] Alle drei Aktionen funktionieren über UI und API, Swagger ist aktualisiert.
- [ ] Auth-Risiko (D4) ist von `sec-agent` bewertet.
