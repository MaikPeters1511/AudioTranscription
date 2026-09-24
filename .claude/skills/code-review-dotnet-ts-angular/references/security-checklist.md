# OWASP-orientierte Sicherheits-Checkliste (Code-Ebene)

Diese Liste ergänzt den Dependency-Scan (`security-scan.md`) um die
Schwachstellen, die im eigenen Code entstehen – die findet kein
Vulnerability-Scanner, nur Lesen mit der richtigen Frage im Kopf: "Was
passiert hier, wenn der Input böswillig ist?"

Pro Punkt: kurz prüfen, ob zutreffend; nur bei konkretem Fund einen
Bericht-Eintrag erzeugen (keine Checkliste-Abhak-Prosa im Bericht).

## A01 – Broken Access Control
- Fehlt `[Authorize]` auf einem Endpoint, der es haben sollte? Laut
  `CLAUDE.md` ist `[Authorize]` der Default; jedes `[AllowAnonymous]` muss
  bewusst und begründbar sein – prüfen, ob es das ist.
  Fehlerhafte Objektreferenzen: prüft die Query, ob der eingeloggte User
  tatsächlich Owner/berechtigt für die angefragte ID ist (IDOR), oder wird
  z. B. `orderId` ungeprüft aus der Route übernommen?
- Frontend: Route Guards vorhanden, aber serverseitig nicht durchgesetzt?
  Client-seitige Checks allein sind kein Access Control.

## A02 – Cryptographic Failures / Secrets
- Hardcoded Secrets, Connection Strings, API-Keys im Code (auch in
  Testdateien, `appsettings.json` ohne Umgebungsvariable, `.env` im
  Repo)? Verstößt direkt gegen `CLAUDE.md`.
- Sensible Daten im Klartext geloggt oder in Fehlermeldungen ausgegeben?

## A03 – Injection
- Rohe SQL-Strings mit String-Interpolation statt parametrisierter Queries/
  EF-Core-LINQ?
- Angular: `[innerHTML]`-Bindings mit ungeprüftem User-Input, `bypassSecurityTrust*`-
  Aufrufe ohne echte Notwendigkeit?
- Kommando-/Prozess-Aufrufe mit unbereinigtem User-Input?

## A04/A05 – Insecure Design & Security Misconfiguration
- CORS-Policy zu permissiv (`AllowAnyOrigin` + Credentials)?
- Fehlende Security-Header (CSP, X-Content-Type-Options) in der
  Presentation-Schicht?
- Debug-/Entwicklungsmodus-Konfiguration, die in Produktion aktiv bleiben
  könnte (detaillierte Fehlerseiten, Swagger ohne Schutz in Prod)?

## A06 – Vulnerable Components
→ siehe `security-scan.md` (separater, verbindlicher Schritt).

## A07 – Identification & Authentication Failures
- JWT-Validierung: wird Issuer/Audience/Lifetime tatsächlich geprüft, oder
  nur die Signatur?
- Passwort-/Token-Handling: Klartext-Vergleich statt konstantzeitig?
- Session-/Token-Ablauf sinnvoll gesetzt, Refresh-Token-Rotation vorhanden?

## A08 – Software & Data Integrity Failures
- Deserialisierung von nicht vertrauenswürdigen Daten ohne Typ-Whitelisting
  (`TypeNameHandling`-artige Muster, unsichere `JsonSerializer`-Konfiguration)?

## A09 – Logging & Monitoring
- Leere `catch`-Blöcke (laut `CLAUDE.md` explizit untersagt) oder
  verschluckte Exceptions ohne Log?
- Werden Stacktraces/DB-Fehler an den Client durchgereicht statt zentral
  über Middleware gefangen?

## A10 – SSRF
- Backend, das URLs aus User-Input direkt anfragt (z. B. Webhook-Ziele,
  Bild-Import), ohne Ziel-Validierung/Allowlist?

## KI-spezifisch (Prompt Injection / Datenschutz)
Passend zum Stack (OpenAI/LMStudio/RAG, siehe `CLAUDE.md`):
- Werden ungefilterte, sensible Domänendaten direkt in Prompts an externe
  APIs geschickt?
- Ist der RAG-Kontext (Qdrant-Retrieval) gegen Prompt-Injection aus
  gespeicherten Dokumenten abgesichert (z. B. Systemprompt trennt klar
  zwischen Instruktion und retrieved content)?
- Fehlt Retry-/Timeout-Handling (Polly o. Ä.) bei KI-API-Aufrufen, sodass ein
  Ausfall der externen API den Request unkontrolliert crashen lässt?
