# ADR 0009: Automatisierung – API-Keys, Webhooks und Ordner-Watch

- **Status:** Akzeptiert
- **Datum:** 2026-11-10
- **Entscheider:** architect-agent, sec-agent; umgesetzt in Story S28 (#164)
- **Baut auf:** [ADR 0003](./0003-authentifizierung.md) (Cookie-Sitzungen), [ADR 0007](./0007-jobs-pro-benutzer.md) (Eigentümer pro Job)

## Kontext

Skripte, n8n oder Zapier sollen die API ohne Browser-Login nutzen, über ein „Fertig“-Ereignis informiert werden und Dateien aus einem überwachten Ordner transkribieren lassen. Alle drei Wege öffnen die Anwendung nach außen (Geheimnisse, ausgehende Aufrufe, Dateisystem) und brauchen deshalb feste Sicherheitsregeln.

## Entscheidung

### 1. API-Keys

1. **Format:** `at_<Präfix>_<Geheimnis>`. Das Präfix besteht aus 8 zufälligen Zeichen (Base62) und ist öffentlich (Anzeige in der Liste, Suche in der DB). Das Geheimnis besteht aus 32 zufälligen Bytes, Base64url-kodiert. Das feste Anfangszeichen `at_` erlaubt Secret-Scannern, Keys zu erkennen.
2. **Nur der Hash wird gespeichert:** SHA-256 über den gesamten Key. Weil das Geheimnis 256 Bit Entropie hat, ist ein langsamer Passwort-Hash (bcrypt/PBKDF2) unnötig; SHA-256 erlaubt die Prüfung ohne spürbare Latenz pro Request. Der Vergleich läuft zeitkonstant (`CryptographicOperations.FixedTimeEquals`). Der Klartext-Key wird genau einmal in der Antwort von `POST /api/api-keys` ausgeliefert und nie wieder.
3. **Übertragung:** `Authorization: Bearer at_…` oder Header `X-Api-Key`. Nie als Query-Parameter (landet in Logs und Verlauf).
4. **Auth-Schema:** Ein eigenes Schema `ApiKey`. Ein Policy-Schema wählt es, wenn der Request einen Key mitbringt, sonst gilt wie bisher die Cookie-Sitzung. Der Principal trägt die Benutzer-ID des Besitzers (`NameIdentifier`), damit [ADR 0007](./0007-jobs-pro-benutzer.md) unverändert greift: Ein Key sieht genau die Jobs seines Besitzers und nicht mehr.
5. **Ablehnung mit 401:** unbekannter, abgelaufener (`ExpiresAt`) oder entzogener (gelöschter) Key sowie ein Key eines deaktivierten Benutzers. Die Antwort verrät nicht, welcher Grund zutraf.
6. **Scopes:** `jobs:read` (GET/HEAD auf Jobs, Suche, Export) und `jobs:write` (Hochladen, Ändern, Löschen). `jobs:write` schließt Lesen nicht ein, beides wird bei Bedarf vergeben. Eine Middleware erzwingt sie zentral (kein verteiltes Einzelprüfen): Erlaubt sind für Keys nur die Pfade `/api/audio-jobs`, `/api/uploads` und `/api/search`; fehlt der Scope, antwortet `403`.
7. **Keine Selbstverwaltung per Key:** `/api/api-keys`, `/api/webhooks`, `/api/admin`, `/api/auth` und alle übrigen Pfade antworten für Keys mit `403`. Ein gestohlener Key kann sich so weder verlängern noch neue Keys oder Webhooks anlegen. Die Verwaltung braucht die Browser-Sitzung.
8. **Ablauf:** optional (`expiresAt`), höchstens 365 Tage. Pro Benutzer höchstens 20 Keys.
9. **Nie in Logs:** Der Key wird nirgends protokolliert, weder im Handler noch in Fehlermeldungen. Geloggt wird höchstens das Präfix. Ein Test prüft das mit einem aufzeichnenden Logger.
10. **`LastUsedAt`** wird höchstens einmal pro Minute und Key geschrieben, damit kein Request einen Schreibzugriff auslöst.
11. **Rate-Limit:** Ein fester Zeitfenster-Limiter begrenzt Requests mit gültigem Key pro Key, Standard 300 pro Minute (`ApiKeys:RequestsPerMinute`). Abgelehnte Keys zählen pro Remote-IP, nicht pro Präfix: Das Präfix ist nicht geheim, und wer es kennt, soll das Budget des echten Keys nicht aufbrauchen können. Zusätzlich begrenzt ein Limiter `POST /api/webhooks/{id}/test` pro Benutzer auf 10 pro Minute. Der Limiter läuft nach der Authentifizierung (der Webhook-Test-Limiter braucht den Benutzer); ungültige Keys kosten höchstens einen indizierten Lookup nach dem Präfix und werden pro Remote-IP gezählt (hinter einem Proxy ohne `X-Forwarded-For`-Auswertung teilen sie sich einen Topf, der gültige Keys nie berührt). Brute Force gegen das Geheimnis ist wegen 256 Bit nicht praktikabel; das Limit schützt vor Last.

### 2. Webhooks

1. **Ereignisse:** `job.completed` und `job.failed` (abgebrochene Jobs lösen nichts aus) sowie `webhook.test`. Der Besitzer des Jobs bekommt die Ereignisse seiner aktiven Webhooks; Freigaben lösen keine Webhooks aus.
2. **Nutzdaten ohne Inhalt:** `{ "event", "deliveryId", "createdAt", "data": { jobId, fileName, status, language, durationSeconds, completedAt } }`. Das Transkript selbst wird nicht gesendet (Datenschutz); der Empfänger holt es bei Bedarf über die API mit einem Key. Eine Fehlermeldung des Jobs wird ebenfalls nicht mitgegeben.
3. **Signatur:** Pro Webhook gibt es ein serverseitig erzeugtes Geheimnis (`whsec_` + 32 Zufallsbytes, Base64url), das einmal bei Anlage angezeigt wird. Jede Zustellung trägt
   - `X-Webhook-Id`: ID der Zustellung (für Deduplizierung beim Empfänger),
   - `X-Webhook-Timestamp`: Unix-Sekunden,
   - `X-Webhook-Signature`: `v1=` + Hex-HMAC-SHA256 über `"{Timestamp}.{Body}"` mit dem Geheimnis.
   Der Zeitstempel ist Teil der Signatur, damit der Empfänger alte Aufzeichnungen (Replay) ablehnen kann. Die Prüfung beim Empfänger muss zeitkonstant erfolgen; die README zeigt ein Beispiel.
4. **Geheimnis im Ruhezustand:** HMAC braucht den Klartext, ein Hash genügt hier nicht. Das Geheimnis wird deshalb mit ASP.NET Core Data Protection verschlüsselt gespeichert und nie über die API zurückgegeben. Die Schlüssel von Data Protection müssen wie bei den Cookie-Sitzungen persistiert sein.
5. **Zustellung mit Wiederholung (Outbox):** Beim Abschluss eines Jobs entsteht pro passendem Webhook eine Zeile `WebhookDeliveries` (Status `Pending`). Ein Hintergrunddienst arbeitet fällige Zeilen ab. Das überlebt Neustarts und blockiert die Transkription nie. Erfolg ist jede 2xx-Antwort. Bei allem anderen (Status ≠ 2xx, Timeout, Verbindungsfehler, Weiterleitung) folgt ein neuer Versuch mit wachsendem Abstand: **30 s, 2 min, 10 min, 1 h, 6 h** (insgesamt 6 Versuche, `Webhooks:RetryDelays` / `MaxAttempts`). Danach steht die Zustellung auf `Failed`. `4xx` außer 408/429 wird nicht wiederholt (der Empfänger hat sie ausdrücklich abgelehnt). Das Protokoll (`GET /api/webhooks/{id}/deliveries`) zeigt Ereignis, Status, Versuche, letzten HTTP-Status und eine **kurze, bereinigte** Fehlerklasse (z. B. `timeout`, `http 500`), nie den Antwortkörper des Ziels und nie Stacktraces. Zustellungen werden nach 30 Tagen gelöscht.
6. **Test:** `POST /api/webhooks/{id}/test` sendet sofort ein `webhook.test` mit genau einem Versuch und liefert die Zustellung zurück.
7. **Grenzen:** höchstens 10 Webhooks pro Benutzer, Timeout 10 s pro Versuch, Antwortkörper werden nicht gelesen.
8. **Eigener HTTP-Client:** Webhooks nutzen keinen Factory-Client. Die Standard-Resilienz (eigene Wiederholungen) und die Service-Discovery der ServiceDefaults sollen hier nicht greifen: Wiederholungen gehören ins Zustellungsprotokoll, und das Ziel muss genau das geprüfte sein.

### 3. SSRF-Schutz für Webhook-Ziele

Webhook-URLs sind vom Benutzer frei wählbar und werden vom Server aufgerufen, damit sind sie der klassische SSRF-Weg. Regeln:

1. **Schema:** nur `https`. `http` ist nur erlaubt, wenn der Betreiber `Webhooks:AllowHttp` setzt. Keine Zugangsdaten in der URL (`user:pass@`), keine Fragmente.
2. **Verbotene Ziele** (beim Speichern geprüft, beim Zustellen **erneut**): Loopback (`127.0.0.0/8`, `::1`), private Netze (`10/8`, `172.16/12`, `192.168/16`), Link-Local (`169.254/16` inkl. Cloud-Metadaten `169.254.169.254`, `fe80::/10`), Carrier-Grade-NAT (`100.64/10`), `0.0.0.0/8`, Multicast, Broadcast, Unique-Local (`fc00::/7`), und IPv4-Adressen dieser Bereiche, die in IPv6 stecken (IPv4-mapped `::ffff:a.b.c.d`, NAT64 `64:ff9b::/96`, 6to4 `2002::/16`, IPv4-compatible `::a.b.c.d`). Hostnamen `localhost`, `*.localhost`, `*.local`, `*.internal`, `*.localdomain` sind ebenfalls verboten.
3. **DNS-Rebinding:** Die Prüfung der IP findet beim **Verbindungsaufbau** statt (`SocketsHttpHandler.ConnectCallback`): Der Name wird aufgelöst, jede Adresse geprüft und genau zu einer geprüften Adresse verbunden. Eine Prüfung nur beim Speichern wäre umgehbar.
4. **Weiterleitungen:** Redirect-Limit ist **0**. Es werden keine Weiterleitungen verfolgt; ein `3xx` zählt als Fehlschlag. Sonst ließe sich ein erlaubtes Ziel auf ein internes umbiegen.
5. **Timeout** 10 s gesamt, kein Proxy, keine Cookies, kein HTTP-Auth.
6. **Betreiberausnahme:** `Webhooks:AllowPrivateTargets` (Standard `false`) erlaubt private Ziele, z. B. ein n8n im selben Docker-Netz. Das ist eine Betreiberentscheidung per Konfiguration, kein Benutzerrecht. Auch dann bleiben Link-Local/Metadaten (`169.254/16`, `fe80::/10`) gesperrt.

### 4. Ordner-Watch

1. **Konfiguration:** `Watch:Folders[]` mit `Path` (absolut), `User` (E-Mail des Zielbenutzers, der Besitzer der Jobs wird) und optional `Model`/`Language`. Der Dienst ist nur aktiv, wenn mindestens ein Ordner konfiguriert ist (`Watch:Enabled`, Standard `true`). Es gibt keine UI und keine API zum Setzen von Ordnern: Ein Pfad auf dem Server ist Betreiber-, nicht Benutzerwissen (sonst Dateisystem-Zugriff über die Web-Oberfläche).
2. **Abfrage statt `FileSystemWatcher`:** Der Dienst scannt alle `Watch:PollIntervalSeconds` (Standard 10). `FileSystemWatcher` ist auf Netzlaufwerken und in Docker-Volumes unzuverlässig und verliert Ereignisse bei Neustarts; das Scannen holt beim Start automatisch Liegengebliebenes nach.
3. **Nur Dateien auf oberster Ebene.** Keine Rekursion, keine Symlinks/Reparse-Points, keine versteckten Dateien (Punkt am Anfang) und keine Unterordner. Das schließt Pfad-Traversal und das Folgen von Links aus dem Ordner heraus aus. Der Dateiname wird nur als `Path.GetFileName` übernommen und zum Anlegen der Zielpfade bereinigt; jeder Zielpfad wird gegen den Ordner geprüft.
4. **Erlaubter Typ:** Dateiendung aus einer Liste (`.mp3 .wav .m4a .mp4 .ogg .webm .mkv .mov`), zusätzlich dieselbe Magic-Bytes-Prüfung wie beim Upload. Größenlimit ist `Upload:MaxFileSizeBytes`. Abgelehnte Dateien wandern nach `rejected/` und tauchen im Log nur mit ID auf (Dateinamen können personenbezogen sein).
5. **Fertig-Erkennung:** Eine Datei wird erst übernommen, wenn sie `Watch:StableSeconds` (Standard 5) unverändert ist. So wird keine halb kopierte Datei gelesen.
6. **Ablauf pro Datei:** prüfen → in den Upload-Speicher kopieren → Job für den Zielbenutzer anlegen und einreihen (dieselbe Logik wie der Upload) → Original nach `processed/` **verschieben**.
7. **Neustart-sicher, keine Doppelverarbeitung:** Die Job-ID wird deterministisch aus Ordner, Dateiname, Größe und Änderungszeit abgeleitet (SHA-256, 16 Byte). Existiert der Job schon (Absturz zwischen Anlegen und Verschieben), wird die Datei nur noch verschoben. Die Eindeutigkeit des Primärschlüssels ist die Sperre, es braucht keine zusätzliche Tabelle.
8. **Zielbenutzer:** Gibt es ihn nicht oder ist er deaktiviert, bleibt der Ordner unberührt und der Fehler wird einmal je Scan geloggt.

## Verworfene Alternativen

- **OAuth/JWT-Client-Credentials statt Keys:** Für Skripte und n8n unverhältnismäßig; Keys sind dort Standard.
- **Argon2/bcrypt für Keys:** Schutz gegen schwache Passwörter, die es bei 256-Bit-Zufall nicht gibt; kostet nur CPU je Request.
- **Keys mit Zugriff auf alle Endpunkte:** Ein gestohlener Key könnte weitere Keys und Webhooks anlegen. Verworfen zugunsten der Pfad-Allowlist.
- **Zustellung im Speicher (Kanal wie die Transkriptionswarteschlange):** Geht beim Neustart verloren. Die DB-Outbox ist robuster und liefert das Zustellungsprotokoll gleich mit.
- **Webhook-Signatur nur über den Body:** Ohne Zeitstempel wären Replays möglich.
- **`FileSystemWatcher`:** siehe oben.
- **Ordner pro Benutzer über die UI:** siehe Ordner-Watch Punkt 1.

## Konsequenzen

- Neue Migration `AddAutomation` (Tabellen `ApiKeys`, `Webhooks`, `WebhookDeliveries`). Bestehende Migrationen bleiben unverändert.
- Die Policy-Schema-Wahl ersetzt nicht die Cookie-Sitzung; SignalR und alle bestehenden Wege bleiben unverändert.
- Mehrere API-Instanzen: Die Zustellung greift auf dieselbe Tabelle zu; eine Zeile kann dann doppelt zugestellt werden (mindestens einmal). Der Empfänger dedupliziert über `X-Webhook-Id`. Der Ordner-Watch gehört nur auf eine Instanz.
- Betreiber müssen Data Protection persistieren, damit Webhook-Geheimnisse nach einem Neustart lesbar bleiben (siehe README).
- Frontend: Seiten „API-Keys“ und „Webhooks“ für angemeldete Benutzer (S28-T5).
