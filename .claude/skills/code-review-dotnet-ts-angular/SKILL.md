---
name: code-review-dotnet-ts-angular
description: >-
  Führt ein umfassendes Code-Review für .NET/C# (Backend), TypeScript und
  Angular (Frontend) durch – inkl. Clean-Architecture-/DDD-Konformität,
  Performance und einer verbindlichen Sicherheitsprüfung auf aktuelle
  Schwachstellen (OWASP Top 10, bekannte CVEs in NuGet/npm-Paketen). Aktiviere
  diesen Skill immer, wenn ein Code-Review, Pull-Request-Review, Merge-Check
  oder eine Sicherheitsprüfung für Backend- (.NET/C#/Wolverine), Frontend-
  (Angular/TypeScript/DaisyUI) oder Fullstack-Code angefragt wird – auch bei
  beiläufigen Formulierungen ohne das Wort "Code-Review": "schau mal über
  meinen PR/Code drüber", "ist das sicher?", "check das mal", "haben wir
  veraltete/verwundbare Pakete", "ist das OWASP-/Clean-Architecture-konform?",
  oder wenn jemand einen Code-Ausschnitt einfügt und nach einer Einschätzung
  fragt. Löst auch bei Erwähnung typischer Dateimuster im Review-Kontext aus
  (`*Controller.cs`, `*Service.cs`, `*Repository.cs`, `*.component.ts`) in
  Kombination mit "review", "PR", "sicher", "mergen" oder "OWASP". Nicht
  auslösen für reine Neu-Implementierung, Konzept-/Lernfragen, Planung oder
  unabhängiges Debugging ohne Review-/Sicherheitsbezug (z. B. "füge Feature X
  hinzu", "erkläre mir switchMap vs. mergeMap", "Sprint planen", "meine
  EF-Core-Migration ist kaputt").
---

# Code Review: .NET, TypeScript & Angular

Dieser Skill führt ein strukturiertes, mehrschichtiges Code-Review durch. Ein
Review ist nur so gut wie seine Beweise: Jeder Befund muss auf eine konkrete
Datei/Zeile zeigen und – wo möglich – durch ein tatsächlich ausgeführtes
Kommando (Build, Test, Vulnerability-Scan) belegt sein. Keine Vermutungen
ohne Kennzeichnung als solche.

## Ablauf

1. **Scope bestimmen.** Standardmäßig den aktuellen Diff/Branch reviewen
   (`git diff main...HEAD` bzw. `git diff --staged`); bei explizitem Wunsch
   die ganze Codebase oder einen genannten Pfad/PR. Frage nach, falls unklar,
   ob nur der Diff oder die betroffenen Dateien vollständig geprüft werden
   sollen – ein Sicherheitsproblem liegt oft außerhalb der geänderten Zeilen
   (z. B. eine Abhängigkeit, die gar nicht im Diff auftaucht).

2. **Sicherheitsscan zuerst ausführen** (siehe
   [references/security-scan.md](references/security-scan.md)). Das ist der
   Teil, der sich nicht durch Lesen des Codes ersetzen lässt: veraltete
   Pakete mit bekannten CVEs findet man nur durch tatsächliches Scannen, nicht
   durch Code-Lesen. Führe je nach Projektinhalt aus:
   - `dotnet list package --vulnerable --include-transitive` in jedem
     Projekt mit `.csproj`
   - `npm audit --omit=dev` (und ggf. ohne `--omit=dev` für volle Sicht) in
     jedem Projekt mit `package.json`
   - Ergänzend eine kurze Web-Recherche zu aktuellen CVEs für Kernpakete
     (z. B. Angular, ASP.NET Core, Wolverine, Identity-Bibliotheken), falls
     WebSearch verfügbar ist – lokale Advisory-Datenbanken hinken neuen
     Lücken oft hinterher.
   Ein Fund aus diesem Schritt ist immer Severity "High" oder "Critical",
   nie kosmetisch.

3. **Statisches Sicherheits-Review des Codes** anhand der OWASP-Top-10-
   Checkliste in [references/security-checklist.md](references/security-checklist.md)
   (Injection, Auth/AuthZ, Secrets, Deserialisierung, XSS/CSRF, SSRF,
   Logging sensibler Daten, ...).

4. **.NET/C#-Review** anhand [references/dotnet-review.md](references/dotnet-review.md):
   Clean Architecture/Vertical-Slice-Grenzen, DDD (Aggregate Roots, Value
   Objects), async/await-Korrektheit, EF-Core-N+1-Probleme, Nullable-
   Reference-Types, Exception-Handling.

5. **TypeScript/Angular-Review** anhand [references/angular-review.md](references/angular-review.md):
   Standalone Components, Signals/Change Detection, RxJS-Subscription-Leaks,
   i18n (keine hardcodierten sichtbaren Strings), ARIA/a11y, Typsicherheit.

6. **Ergebnisse berichten.** Gruppiere Befunde nach Severity
   (Critical → High → Medium → Low → Info), nicht nach Datei. Für jeden
   Befund: Datei:Zeile, was ist das Problem, warum ist es ein Problem
   (Angriffsszenario oder konkreter Bug-Fall, keine generische Floskel),
   und ein konkreter Fix-Vorschlag. Findet der Scan aus Schritt 2 nichts, das
   explizit im Bericht vermerken ("keine bekannten CVEs in den geprüften
   Abhängigkeiten zum Zeitpunkt des Scans") – ein stiller Erfolg ist sonst
   nicht von "nicht geprüft" unterscheidbar.

7. **Rollenklarheit wahren** (siehe `CLAUDE.md`): Dieser Skill review-t und
   schlägt Fixes vor, implementiert sie aber nicht automatisch mit – außer
   der Nutzer bittet explizit darum. Architektur-Grundsatzentscheidungen
   gehören an `architect-agent`, reine Identity-/RBAC-Implementierung an
   `sec-agent` zurück.

## Wann welche Referenzdatei laden

| Situation | Datei |
|---|---|
| Immer zu Beginn eines Reviews mit Abhängigkeitsänderungen oder auf Anfrage "Sicherheitslücken finden" | `references/security-scan.md` |
| Jedes Review (Backend wie Frontend) | `references/security-checklist.md` |
| `.cs`/`.csproj`-Dateien im Scope | `references/dotnet-review.md` |
| `.ts`/`.html` (Angular) im Scope | `references/angular-review.md` |

Bei sehr großem Scope (viele Dateien) zunächst grob mit Grep/Glob
kategorisieren (Backend vs. Frontend, welche Pakete geändert), dann gezielt
die passenden Referenzdateien laden – nicht alle vier auf einmal, wenn nur
Frontend betroffen ist.
