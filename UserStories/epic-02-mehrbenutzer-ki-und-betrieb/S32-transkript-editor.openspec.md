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
