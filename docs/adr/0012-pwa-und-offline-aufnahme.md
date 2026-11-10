# ADR 0012: PWA, Service Worker und Offline-Aufnahme

- **Status:** Akzeptiert
- **Datum:** 2026-11-10
- **Kontext-Referenz:** Epic E-02, Story S33 (#192), baut auf S12 (Aufnahme) und S27 (resumable Upload, [ADR 0006](0006-resumable-upload-spike.md)) auf

## Kontext

Die App soll auf dem Handy installierbar sein und Aufnahmen ohne Verbindung sichern, um sie später hochzuladen. Dabei treffen drei Anforderungen aufeinander: Die App enthält vertrauliche Inhalte (Transkripte, Audio), die Authentifizierung läuft über ein Session-Cookie (ADR 0003), und lokale Speicher sind begrenzt und können vom Browser jederzeit geräumt werden.

## Entscheidungen

### 1. Service Worker nur für die App-Shell

Es wird `@angular/service-worker` (`ngsw`) eingesetzt, konfiguriert in `ngsw-config.json`:

- `assetGroups` enthalten ausschließlich statische, benutzerunabhängige Dateien: `index.html`, Bundles (JS/CSS), Favicon, Manifest, Icons und die Übersetzungsdateien `/i18n/*.json`.
- Es gibt **keine `dataGroups`**. Damit fasst der Service Worker keine API-Antwort an: Alle Requests, die zu keiner Asset-Gruppe passen, gehen unverändert ans Netz.
- `navigationUrls` schließen `/api/**` und `/hubs/**` aus, damit eine Navigation auf einen API-Link (z. B. Export-Download) nicht durch die `index.html` ersetzt wird.
- Ein Unit-Test (`ngsw-config.spec.ts`) sichert diese Regeln ab (keine `dataGroups`, keine API-Muster in `assetGroups`, API in `navigationUrls` ausgeschlossen).
- Der Service Worker wird nur im Produktions-Build aktiviert (`isDevMode()` = aus), Registrierung `registerWhenStable:30000`.

Verworfen: Workbox/eigener Service Worker (mehr Code und Risiko für denselben Nutzen), Caching von Transkripten für Offline-Lesen (Datenschutz, Gegenstand einer eigenen Story).

### 2. Offline-Start trotz Cookie-Session

Beim Offline-Start schlägt `GET /api/auth/me` mit Status 0 fehl. Damit die Aufnahme trotzdem erreichbar ist, merkt sich der `AuthService` nach erfolgreicher Anmeldung nur die **E-Mail-Adresse** (`localStorage`, kein Token, kein Passwort). Ein Netzwerkfehler (Status 0, nicht 401) setzt den Benutzer dann „ungeprüft“ auf diese Adresse. Das öffnet nur die Oberfläche; die API prüft weiterhin jede Anfrage. Ein 401 oder der Logout löschen den Merker. Logout löscht außerdem alle lokalen Aufnahmen (Punkt 4).

### 3. Ablage in IndexedDB

- Datenbank `audio-transcription-offline`, Stores `recordings` (Metadaten: Id, Benutzer, MIME-Typ, Zeitpunkte, Status, Größe) und `chunks` (Schlüssel `[recordingId, index]`, Wert: `Blob`).
- `MediaRecorder` liefert alle 5 s einen Abschnitt (`start(timeslice)`); jeder Abschnitt wird sofort geschrieben. Bei Absturz oder Tab-Schließen bleibt die Aufnahme bis auf höchstens 5 s erhalten. Die WebM-/MP4-Fragmente sind aneinandergehängt abspielbar.
- **Statusmodell:** `recording` → `draft` (gestoppt) → `pending` (zum Upload vorgemerkt) → `uploading` → gelöscht; bei Fehler `failed`. Beim App-Start werden `recording` und `draft` zu `pending` „wiederhergestellt“, damit nichts still verloren geht.
- Der Zugriff läuft über die abstrakte Klasse `RecordingStore` (Angular-DI). Tests nutzen `fake-indexeddb` für die echte Implementierung und einen In-Memory-Store für Komponenten- und Service-Tests; kein `vi.mock` auf ein Paket.
- Jede Aufnahme gehört einem Benutzer (E-Mail); die Warteschlange zeigt und lädt nur Aufnahmen des angemeldeten Benutzers hoch.

### 4. Speicherlimits und Datenschutz

- `QuotaExceededError` (und jeder andere Schreibfehler) beim Sichern eines Abschnitts beendet die Aufnahme kontrolliert: Das bisher Aufgenommene bleibt im Speicher und in der Datenbank erhalten, die UI meldet „Speicher voll“ mit Handlungsempfehlung. Es gibt kein stilles Verwerfen.
- Beim Start der Aufnahme fragt die App `navigator.storage.persist()` an (best effort, ohne Prompt-Zwang), damit der Browser die Daten nicht räumt.
- Nach erfolgreichem Upload wird die lokale Aufnahme sofort gelöscht. Manuelles Löschen ist jederzeit möglich.
- Logout mit offenen lokalen Aufnahmen verlangt eine Bestätigung; bei „Ja“ werden sie gelöscht, bei „Nein“ bleibt die Sitzung bestehen. Die UI weist darauf hin, dass Aufnahmen unverschlüsselt im Browser des Geräts liegen. Eine clientseitige Verschlüsselung wurde erwogen, aber verworfen: Ein Schlüssel müsste auf demselben Gerät liegen und böte keinen realen Schutz.
- Läuft die Sitzung ab, bleiben die Aufnahmen erhalten und werden nach erneuter Anmeldung desselben Benutzers hochgeladen.

### 5. Hintergrund-Upload

Ein `OfflineUploadQueueService` verarbeitet `pending`-Aufnahmen nacheinander, sobald Verbindung und Anmeldung bestehen (`online`-Ereignis und beim Start), über `AudioJobService.uploadFile`; damit greift ab 10 MiB automatisch der resumable tus-Upload aus S27. Fehlgeschlagene Uploads werden bei der nächsten Verbindung und manuell („Erneut versuchen“) wiederholt. „Hintergrund“ heißt hier: im geöffneten Tab. Background Sync API wird bewusst nicht genutzt (nur Chromium, und der Upload bräuchte dann authentifizierte Requests im Service Worker – widerspricht Punkt 1).

## Konsequenzen

- Installierbar nur über HTTPS (oder `localhost`); das gilt auch für den Service Worker und `getUserMedia`. In README dokumentiert.
- Neue Version der App wird über `ngsw.json` erkannt; `ngsw-worker.js`, `ngsw.json` und das Manifest werden von nginx mit `no-cache` ausgeliefert.
- Bekannte Grenze: Läuft in einem zweiten Tab eine Aufnahme, wird sie erst als abgebrochen übernommen, wenn 15 s lang kein Abschnitt geschrieben wurde (Abschnitte kommen alle 5 s). Die aktuell im selben Tab laufende oder angezeigte Aufnahme wird nie übernommen. Ein Aufnahmeabbruch hinterlässt höchstens 5 s Verlust.
