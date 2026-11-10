# Story S32: Transkript-Editor mit Korrektur und Versionierung

> Epic: [E-02](./00-epic.openspec.md) · Phase 2 · Agenten: backend-agent, frontend-agent, ux-agent · Größe: L

## 1. Description
Benutzer korrigieren Erkennungsfehler direkt im Player-Kontext. Änderungen werden versioniert, das Roh-Transkript bleibt unverändert erhalten (Prinzip aus S04).

## 2. User Stories
- Als Benutzer möchte ich einzelne Segmente korrigieren, während ich das Audio höre, damit das Transkript korrekt wird.
- Als Benutzer möchte ich frühere Fassungen ansehen und wiederherstellen, damit keine Korrektur verloren geht.

## 3. API & Backend Requirements
- **Modelle (neue Migration):** `SegmentEdit` (SegmentId, Version, Text, EditedBy, EditedAt); `TranscriptSegment.CurrentText` bzw. aktuelle Version.
- **Endpoints:** `PUT /api/jobs/{id}/segments/{segmentId}` (mit Versions-/ETag-Prüfung gegen Überschreiben), `GET .../history`, `POST .../restore`.
- Exporte, Suche und Player nutzen die korrigierte Fassung; das Original bleibt abrufbar.

## 4. Tasks

### S32-T1 Backend: Edit und Versionierung · backend · M
- **AC (TDD):** Konfliktfall (gleichzeitige Bearbeitung → 409), Verlauf vollständig, Wiederherstellen erzeugt neue Version, Berechtigung nur für Eigentümer (S19).

### S32-T2 Suche und Export anpassen · backend · S
- **AC:** Volltextindex aktualisiert sich bei Korrektur, Exporte (S06/S21) enthalten den korrigierten Text.

### S32-T3 Frontend: Inline-Editor im Player · frontend, ux · L
- **AC:** Segment per Doppelklick/Enter editierbar, Abspielen des Segments per Tastenkürzel, Speichern per Debounce oder explizit, Fehler sauber angezeigt, vollständige Tastaturbedienung, DE/EN, Unit-Tests für Edit-State.

### S32-T4 Frontend: Versionsverlauf · frontend · S
- **AC:** Verlauf pro Segment mit Diff-Hervorhebung und „Wiederherstellen“.

## 5. Acceptance Criteria (DoD)
- [ ] Original und Korrekturen sind getrennt, Roh-Transkript wird nie überschrieben.
- [ ] E2E: Segment korrigieren, Suche findet neuen Text, Export enthält ihn.

## 6. Umsetzung

- **Getrennte Ablage:** `TranscriptSegment.Text` bleibt das unveränderte Original, die Korrektur steht in `CurrentText` (Fassung `EffectiveText = CurrentText ?? Text`); `Version` zählt die Korrekturen (0 = Original) und dient als Konfliktschutz. Die Historie liegt in `SegmentEdit` (Migration `AddSegmentEdits`, eindeutiger Index auf `SegmentId` + `Version`). Version 0 wird nicht doppelt gespeichert, sie ist das Original am Segment.
- **Endpoints:** `PUT /api/audio-jobs/{id}/segments/{index}`, `GET .../history`, `POST .../restore` (statt `/api/jobs/...` und `segmentId`, passend zu den übrigen Job-Routen, Segmente werden über ihren `index` angesprochen). Statt eines ETag-Headers gilt `expectedVersion` im Body; eine veraltete Version ergibt `409`. Bearbeiten und Wiederherstellen brauchen Eigentümerrechte (S19), den Verlauf sehen auch Leser geteilter Jobs.
- **Suche/Export:** Der Volltextindex der Segmente (S13) wurde um `CurrentText` erweitert (in der Migration, wie der ursprüngliche Index nur wenn Full-Text installiert ist). Ein korrigiertes Segment wird nur über die Korrektur gefunden, nicht mehr über das ersetzte Original. Exporte und Untertitel (S06/S21) nutzen `EffectiveText`; Roh-Transkript, Varianten und deren Suche bleiben unverändert.
- **Frontend:** Im Player öffnen Enter, Doppelklick oder der ✎-Knopf den Editor (Strg+Enter speichert, Esc bricht ab, Alt+P spielt das Segment ab); gespeichert wird explizit, kein Debounce. Fehler (Konflikt, sonstige) erscheinen als `role="alert"` ohne technische Details, der Entwurf bleibt erhalten. Der Verlauf (`<app-segment-history>`) zeigt jede Fassung mit Wortdiff gegenüber der vorherigen und „Wiederherstellen“. Leser geteilter Jobs bekommen keine Bearbeitungs- und Wiederherstellen-Knöpfe.
- **Nicht umgesetzt/offen:** Der Segmenttext (Zeitgrenzen, Sprecher) ist nicht änderbar, nur der Text. Der E2E-Test (`transcript-editor.mocked.spec.ts`) läuft nur in der CI, Playwright-Browser sind lokal nicht installiert; dass Suche und Export den neuen Text enthalten, deckt der Backend-Integrationstest ab (`SegmentEditIntegrationTests`, `SearchIntegrationTests` gegen echtes SQL Server).
