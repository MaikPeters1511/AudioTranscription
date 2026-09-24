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
  - [x] Test mit Fake-Processor oder Adapter: Die Werte kommen in `IProgress` an.

### S07-T2 Drosselung und SignalR-Push im Worker · backend · S
- Nur senden, wenn sich der Wert um mindestens 1 % geändert hat und mindestens 250 ms seit dem letzten Senden vergangen sind. 100 % wird immer gesendet.
- **AC (TDD):**
  - [x] Test: 1000 Rohwerte führen zu höchstens 101 Events.
  - [x] Test: Der letzte Wert ist 100.

### S07-T3 Frontend: Fortschrittsbalken · frontend · S
- `signalr.service.ts` abonniert `JobProgress`, Speicherung pro Job als Signal. In Liste und Detailansicht: DaisyUI `<progress class="progress">` mit `aria-valuenow`/`aria-valuemin`/`aria-valuemax` und i18n-Label. Bei `Processing` ohne Wert wird ein unbestimmter Balken angezeigt.
- **AC:**
  - [x] Unit-Test: Das Event aktualisiert den Balken.
  - [x] Screenreader liest den Fortschritt höchstens alle 10 % vor (`aria-live="polite"` gedrosselt).

## 5. Umsetzungsnotizen (2026-09-24)
- **Service (T1):** Die Signatur ist `TranscribeAsync(path, settings, IProgress<int>? progress, ct)`. `WhisperProgress.Handler` macht aus einem `IProgress<int>` den `OnProgressHandler` von Whisper.net und begrenzt die Werte auf 0–100. Test: Die Werte kommen unverändert bzw. begrenzt an. Nach der letzten Verarbeitung meldet der Service immer 100, weil Whisper den letzten Prozentpunkt nicht zuverlässig meldet.
- **Drosselung (T2):** `ThrottledProgress` leitet einen Wert nur weiter, wenn er gestiegen ist und seit dem letzten Senden mindestens 250 ms vergangen sind; 100 immer, aber nur einmal. Er arbeitet synchron und mit Lock. `Progress<T>` würde die Werte auf den Thread-Pool posten und so die Reihenfolge verlieren.
- **Push (T2):** Der Worker sendet `JobProgress { jobId, percent }` per SignalR ohne `await`, weil der Callback auf dem Whisper-Thread läuft. Sendefehler werden als Warnung geloggt.
- **Letzter Wert im Speicher:** `JobProgressStore` (Singleton) hält den letzten Wert, es gibt keinen DB-Schreibvorgang. Er wird am Ende jedes Jobs entfernt (`finally`), auch bei Fehler oder Abbruch.
- **Neues Feld `progressPercent`:** `GET /api/audio-jobs` und `GET /api/audio-jobs/{id}` liefern es für laufende Jobs. Wer die Seite neu lädt oder sich neu verbindet, sieht sofort den aktuellen Wert. Für andere Status ist das Feld immer leer, auch wenn im Speicher noch ein veralteter Wert steht (Test).
- **Frontend (T3):**
  - `JobProgressComponent` zeigt einen DaisyUI-`progress` mit `aria-valuenow`/`-min`/`-max` und i18n-Label. Ohne Wert ist der Balken unbestimmt (kein `value`).
  - Mit `announce` gibt es eine `aria-live="polite"`-Region. Ihr Text ändert sich nur in 10-%-Schritten, unter 10 % bleibt sie leer.
  - Angekündigt wird nur in der Detailansicht. In der Liste gibt es nur den Balken, damit mehrere Zeilen nicht durcheinander sprechen.
  - Der `AudioJobService` hält den Fortschritt pro Job als Signal. Er übernimmt ihn aus Liste, Detail und dem Event `JobProgress`, ignoriert niedrigere, verspätete Werte und verwirft den Wert, sobald der Job nicht mehr `Processing` ist.
- **Nebenbei (S08):** Die Sprachkarte der Detailansicht zeigte während der Verarbeitung „Nicht erkannt“ und darunter „Automatisch erkannt“. Der Hinweis erscheint jetzt erst, wenn es eine Sprache gibt.
- **Offen (DoD):** Der Test mit einer mehrminütigen Datei braucht ein Whisper-Modell. Deren Download ist in der Entwicklungsumgebung gesperrt. Geprüft sind die Darstellung in Liste und Detail sowie der Ablauf vom Worker bis zum Balken: per Unit- und Integrationstests und im Browser mit gemockter API.

## 6. Acceptance Criteria (DoD)
- [ ] Bei einer mehrminütigen Datei steigt der Balken sichtbar und stetig an.
