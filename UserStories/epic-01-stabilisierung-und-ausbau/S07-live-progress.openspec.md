# Story S07: Live-Fortschritt in Prozent

> Epic: [E-01](./00-epic.openspec.md) · GitHub: #12 · Phase 2 · Agenten: backend-agent, frontend-agent, ux-agent

## 1. Description
Während der Transkription zeigt das Frontend nur „Processing“. Whisper.net bietet `WithProgressHandler(Action<int>)`. Den Fortschritt übertragen wir per SignalR und zeigen ihn als Fortschrittsbalken an.

## 2. User Stories
- Als Nutzer möchte ich den Fortschritt in Prozent sehen, damit ich einschätzen kann, wie lange die Transkription noch dauert.

## 3. API & Backend Requirements
- **SignalR-Event:** `JobProgress { jobId: Guid, percent: int }`.
- Kein DB-Schreibvorgang pro Prozentpunkt. Optional wird der letzte Wert im Speicher gehalten, damit neu verbundene Clients ihn erhalten.

## 4. Tasks

### S07-T1 Progress-Callback im Transkriptionsservice · backend · S
- `TranscribeAsync(..., IProgress<int>? progress, ...)` und `WithProgressHandler`, der an `progress.Report` weiterleitet.
- **AC:**
  - [ ] Test mit Fake-Processor oder Adapter: Die Werte kommen in `IProgress` an.

### S07-T2 Drosselung und SignalR-Push im Worker · backend · S
- Nur senden, wenn sich der Wert um mindestens 1 % geändert hat und mindestens 250 ms seit dem letzten Senden vergangen sind. 100 % wird immer gesendet.
- **AC (TDD):**
  - [ ] Test: 1000 Rohwerte führen zu höchstens 101 Events.
  - [ ] Test: Der letzte Wert ist 100.

### S07-T3 Frontend: Fortschrittsbalken · frontend · S
- `signalr.service.ts` abonniert `JobProgress`, Speicherung pro Job als Signal. In Liste und Detailansicht: DaisyUI `<progress class="progress">` mit `aria-valuenow`/`aria-valuemin`/`aria-valuemax` und i18n-Label. Bei `Processing` ohne Wert wird ein unbestimmter Balken angezeigt.
- **AC:**
  - [ ] Unit-Test: Das Event aktualisiert den Balken.
  - [ ] Screenreader liest den Fortschritt höchstens alle 10 % vor (`aria-live="polite"` gedrosselt).

## 5. Acceptance Criteria (DoD)
- [ ] Bei einer mehrminütigen Datei steigt der Balken sichtbar und stetig an.
