# Story S33: PWA und Offline-Aufnahme

> Epic: [E-02](./00-epic.openspec.md) · Phase 3 · Agenten: frontend-agent, ux-agent · Größe: L · Voraussetzung: S12, S27 (empfohlen)

## 1. Description
Die App wird installierbar (PWA). Aufnahmen am Handy werden lokal zwischengespeichert und bei bestehender Verbindung hochgeladen.

## 2. User Stories
- Als Benutzer möchte ich die App auf dem Handy installieren, damit ich sie wie eine normale App starte.
- Als Benutzer möchte ich ohne Verbindung aufnehmen und später hochladen, damit nichts verloren geht.

## 3. UI/UX Requirements
- **Komponenten:** Installationshinweis, Offline-Banner, Liste „Wartet auf Upload“.
- **State:** Verbindungsstatus, lokale Upload-Warteschlange (IndexedDB).

## 4. Tasks

### S33-T1 PWA-Grundlagen · frontend · S
- Web-App-Manifest, Icons, Angular Service Worker nur für die App-Shell (keine Caches mit Nutzdaten oder API-Antworten).
- **AC:** Installierbar (Lighthouse-PWA-Check), HTTPS-Voraussetzung dokumentiert.

### S33-T2 Lokale Aufnahmespeicherung · frontend · M
- **AC (TDD):** Aufnahme wird abschnittsweise in IndexedDB gesichert, Wiederherstellung nach Absturz/Tab-Schließen, Speicherplatz-Fehler werden sauber gemeldet.

### S33-T3 Hintergrund-Upload · frontend · M
- **AC:** Upload startet bei Verbindung (nutzt S27, falls vorhanden), Retry, Abbrechen/Löschen lokaler Aufnahmen, Fortschritt sichtbar.

### S33-T4 Datenschutz bei lokaler Speicherung · sec, frontend · S
- **AC:** Lokale Aufnahmen werden nach Upload gelöscht, Logout entfernt nicht hochgeladene Daten nach Rückfrage, Hinweis in der UI.

### S33-T5 UX-Review · ux · S
- **AC:** Bedienung auf kleinen Bildschirmen geprüft, Touch-Ziele ≥ 44 px, Screenreader-Test der Aufnahmesteuerung.

## 5. Acceptance Criteria (DoD)
- [ ] Aufnahme im Flugmodus, anschließender Upload nach Verbindungswiederkehr (E2E mit simuliertem Offline-Modus).
- [ ] DE/EN-Texte, Tastatur- und ARIA-Konformität.
