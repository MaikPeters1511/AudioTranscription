# Sicherheitsscan: Abhängigkeiten auf bekannte Schwachstellen prüfen

Ziel dieses Schritts: bekannte CVEs in eingesetzten Paketen finden, bevor der
Code überhaupt inhaltlich gelesen wird. Diese Lücken sind durch reines
Code-Lesen praktisch nie erkennbar – sie stecken im Paket, nicht im
eigenen Code.

## .NET / NuGet

In jedem Verzeichnis mit einer `.sln` oder `.csproj` ausführen:

```bash
dotnet restore
dotnet list package --vulnerable --include-transitive
```

- `--include-transitive` nicht weglassen: die meisten realen Lücken stecken
  in transitiven Abhängigkeiten, nicht in den direkt referenzierten Paketen.
- Ein leeres Ergebnis ("no vulnerable packages") ist ein valider, meldenswerter
  Befund – kein Grund, den Schritt zu überspringen oder unerwähnt zu lassen.
- Schlägt `dotnet restore` fehl (fehlende SDK-Version, kein Netzwerk), das
  explizit im Bericht vermerken statt den Scan stillschweigend auszulassen.

## TypeScript / Angular (npm)

Im Verzeichnis mit `package.json`:

```bash
npm audit --omit=dev
npm audit
```

- Erst `--omit=dev` für die produktionsrelevante Sicht, danach ohne Flag für
  das volle Bild (Dev-Dependencies wie Build-Tools sind seltener, aber nicht
  nie ein Angriffsvektor, z. B. bei kompromittierten CI-Pipelines).
- `npm audit fix` **nicht** automatisch ausführen – das kann Breaking
  Changes einspielen. Nur als Vorschlag im Bericht nennen, Ausführung dem
  Nutzer überlassen (siehe explizite Freigabe für Code-Änderungen).
- Bei Yarn/pnpm-Projekten die passenden Äquivalente nutzen (`yarn npm audit`,
  `pnpm audit`).

## Aktuelle CVEs jenseits der lokalen Datenbank

`npm audit` und `dotnet list package --vulnerable` kennen nur, was in ihrer
jeweiligen Advisory-Datenbank bereits erfasst ist – das kann bei sehr neuen
Lücken (0-day, kürzlich veröffentlicht) hinterherhinken. Wenn WebSearch
verfügbar ist und sicherheitskritische Kernpakete im Spiel sind (Angular,
ASP.NET Core, Microsoft Identity/Entra ID, Wolverine, Npgsql, JWT-Bibliotheken,
Authentifizierungs-Middleware), ergänzend kurz recherchieren:

- Suchbegriffe: `"<Paketname>" CVE 2026`, `"<Paketname>" security advisory`,
  `github.com/advisories <Paketname>`.
- Nur Treffer aus den letzten ~12 Monaten und mit Bezug zur tatsächlich
  eingesetzten Version werten – nicht jede alte CVE ist relevant, wenn die
  Version bereits gepatcht ist.
- Quelle im Bericht nennen (Advisory-ID/Link), damit der Befund nachprüfbar
  ist statt einer bloßen Behauptung.

## Was danach passiert

Jeder gefundene CVE-Eintrag wird als eigener Befund mit Severity
Critical/High (je nach CVSS-Score, falls angegeben) in den Review-Bericht
übernommen: betroffenes Paket, installierte vs. gepatchte Version, kurze
Beschreibung der Lücke, Fix (i. d. R. Versionsupdate). Ein Versionsupdate
selbst wird nicht automatisch durchgeführt, nur vorgeschlagen.
