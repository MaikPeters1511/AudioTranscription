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

## 6. Umsetzung

Architekturentscheidungen: [ADR 0012](../../docs/adr/0012-pwa-und-offline-aufnahme.md).

- **S33-T1 PWA-Grundlagen:** `@angular/service-worker` (nur Produktions-Build), `ngsw-config.json` mit einer Asset-Gruppe für die App-Shell (inkl. `/i18n/*.json`, damit die App offline in DE und EN startet) und **ohne `dataGroups`**; `navigationUrls` schließen `/api/**` und `/hubs/**` aus. Ein Test (`pwa-config.spec.ts`) sichert das ab. Web-App-Manifest (`public/manifest.webmanifest`, Start-URL `/upload`, Standalone), Icons (192/512, maskable, Apple-Touch) und `theme-color` in `index.html`. nginx liefert `ngsw-worker.js`, `ngsw.json` und das Manifest mit `no-cache`. HTTPS-Voraussetzung im README dokumentiert. Die Icons sind selbst erzeugte Platzhalter (weißes Mikrofon auf Primärfarbe), ein Branding fehlt noch.
- **S33-T2 Lokale Aufnahmespeicherung:** `AudioRecorderService` fordert alle 5 s einen Abschnitt an und schreibt ihn über `RecordingStore` (IndexedDB, Stores `recordings` und `chunks`) sofort weg. Zustände `recording → draft → pending → uploading`, bei Fehler `failed`. Wiederherstellung: beim Start (und einmalig verzögert) werden abgebrochene Aufnahmen (`recording` ohne Schreibzugriff seit 15 s, `draft`, `uploading`) in die Warteschlange übernommen. Speicherfehler: `QuotaExceededError` beendet die Aufnahme kontrolliert und meldet „Speicher voll“, andere Fehler melden „nicht zwischengespeichert“, die Aufnahme läuft weiter. Ohne IndexedDB fällt der Store auf Arbeitsspeicher zurück. Zusätzlich `navigator.storage.persist()` (best effort).
- **S33-T3 Hintergrund-Upload:** `OfflineUploadQueueService` lädt `pending`-Aufnahmen nacheinander über `AudioJobService.uploadFile` hoch (damit greift S27 ab 10 MiB), sobald Verbindung (`ConnectivityService`) und Anmeldung bestehen. Netzfehler werden bei der nächsten Verbindung automatisch wiederholt, andere Fehler zeigen die Ursache mit „Erneut versuchen“; ein 401 lässt die Aufnahme bis zur nächsten Anmeldung warten. Die Liste „Wartet auf Upload“ (`PendingUploadsComponent`) zeigt Fortschritt, Retry und Löschen (mit zweiter Bestätigung). „Hintergrund“ heißt: im geöffneten Tab; die Background-Sync-API wird bewusst nicht genutzt (ADR 0012).
- **S33-T4 Datenschutz:** Lokale Aufnahmen werden nach erfolgreichem Upload gelöscht; jede gehört einem Benutzer und wird nur ihm gezeigt und für ihn hochgeladen. Abmelden mit lokalen Aufnahmen öffnet einen modalen Dialog (`LogoutConfirmDialogComponent`, Fokus auf „Angemeldet bleiben“, Escape bricht ab), „Löschen und abmelden“ entfernt sie. Hinweis zur unverschlüsselten Ablage steht in der Liste. Für den Offline-Start merkt der `AuthService` nur die E-Mail des letzten Benutzers (Netzfehler Status 0, nie bei 401).
- **S33-T5 UX/a11y:** Touch-Ziele `min-h-11` (44 px), Aufnahmeknopf 56 px; Offline-Banner als `role="status"`, Listen als `section` mit `aria-labelledby`, Schaltflächen mit Namen inkl. Aufnahmedatum, Warnungen als `role="alert"`, alles per Tastatur bedienbar. **Nicht erfolgt:** ein echter Screenreader-Test (VoiceOver/TalkBack) und ein Test auf einem realen Handy; geprüft wurden nur die ARIA-Struktur in den Tests und der E2E-Ablauf in Chrome.
- **Tests:** Unit-Tests für Store (IndexedDB über `fake-indexeddb` und In-Memory), Recorder, Warteschlange, Verbindung, Installations-Service, Komponenten und PWA-Konfiguration. Playwright-Test `offline-recording.mocked.spec.ts` (Mikrofon im Browser simuliert): Aufnahme im Offline-Modus, Speichern, Upload nach Verbindungswiederkehr, Löschen mit Bestätigung, Abmelden mit Rückfrage. Lokal in Chrome grün, in der CI zusätzlich geprüft.
- **Bekannte Grenzen / offen:** Der Service Worker selbst (Installierbarkeit per Lighthouse, Update-Verhalten, echter Offline-Start aus dem Cache) wurde nicht im Browser gegen den Produktions-Build geprüft; der Build erzeugt `ngsw.json` und das Manifest. Aufnahmen sind lokal nicht verschlüsselt. In zwei Tabs gleichzeitig kann eine laufende Aufnahme des ersten Tabs nach 15 s ohne Schreibzugriff als abgebrochen gelten. Der Installationshinweis nutzt `beforeinstallprompt` (Chromium); unter iOS bleibt nur der manuelle Weg.
