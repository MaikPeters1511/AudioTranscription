# ADR 0002: Frontend-i18n mit Transloco (Runtime)

- **Status:** Akzeptiert
- **Datum:** 2026-09-24
- **Kontext-Referenz:** Epic E-01, Enabler EN-1 (#3)

## Kontext

CLAUDE.md verlangt ein strikt mehrsprachiges Frontend (DE/EN) ohne hartcodierte sichtbare Texte. Das Web-Projekt hatte bis dahin keine i18n-Infrastruktur, alle Texte waren deutsch im Template.

Zur Wahl standen:

1. **`@angular/localize`** (offiziell, Übersetzung zur Build-Zeit): Pro Sprache entsteht ein eigenes Bundle. Umschalten heißt Seitenwechsel auf `/de/` bzw. `/en/`. Das braucht Routing pro Locale in nginx und Docker. Unter Aspire liefert `ng serve` nur eine Locale aus.
2. **Runtime-Bibliothek (Transloco, `@jsverse/transloco`):** ein Bundle, Übersetzungen als JSON zur Laufzeit geladen, Sprachwechsel ohne Reload.
3. **ngx-translate:** funktional ähnlich wie Transloco, aber mit weniger Werkzeugen für Tests und Konfiguration.

## Entscheidung

Transloco 8 als Runtime-i18n:

- **Übersetzungen:** `AudioTranscription.Web/public/i18n/{de,en}.json`, geladen über `TranslocoHttpLoader`.
- **Sprachwahl** (`detectLanguage`): Die gespeicherte Wahl (`localStorage["lang"]`) hat Vorrang. Danach zählt die erste unterstützte Browser-Sprache ohne Region, sonst gilt **Deutsch**.
- **Umschalten:** Der `LanguageService` setzt die aktive Sprache, speichert die Wahl und aktualisiert `<html lang>`. Der Umschalter sitzt in der Navbar.
- **Templates:** Die Pipe `transloco` wird genutzt, im TypeScript `TranslocoService.translate` (z. B. für Toasts). Tab-Titel setzt der `PageTitleService`, der sie auch bei einem Sprachwechsel aktualisiert.
- **Datumsformate:** Die Formatstrings (`format.*`) liegen ebenfalls in den Übersetzungsdateien.

## Begründung

- Die Sprache ist zur Laufzeit umschaltbar, wie EN-1 es verlangt.
- Es gibt nur einen Build. Hosting (nginx, Docker, Aspire) bleibt unverändert, die JSON-Dateien werden wie jede statische Datei ausgeliefert.
- `TranslocoTestingModule` lädt in Vitest die echten Übersetzungsdateien (`translocoTesting()` in `src/app/i18n/transloco-testing.ts`).

## Konsequenzen

- Neue sichtbare Texte, ARIA-Labels und `title`-Attribute kommen **immer** in beide Dateien. Die Tests in `translations.spec.ts` prüfen, dass DE und EN dieselben Schlüssel und dieselben Interpolationsparameter haben und keine Werte leer sind.
- Fehlertexte der API (z. B. `ProblemDetails.detail`, `ErrorMessage` eines Jobs) werden unverändert angezeigt und nicht übersetzt. Eine Übersetzung bräuchte stabile Fehlercodes im Backend (eigene Story).
- Das statische `index.html` behält `lang="de"`, `<title>` und `description` für den ersten Paint und für Crawler. Beim Start setzt die App `lang` und den Titel passend zur erkannten Sprache.
