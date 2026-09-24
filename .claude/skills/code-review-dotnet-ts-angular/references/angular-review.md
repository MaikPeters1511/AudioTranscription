# TypeScript 6 / Angular 22 Review-Checkliste

Bezug: `CLAUDE.md` (Angular 22 Standalone Components, DaisyUI 5, i18n DE/EN,
ARIA/Accessibility, TDD).

## Components & Architektur
- **Standalone Components** – kein unnötiges `NgModule` für neue
  Komponenten?
- Selektoren/Dateinamen in `kebab-case` (`user-profile.component.ts`,
  `<app-user-profile>`), Klassen in `PascalCase` (`CLAUDE.md`-Konvention)?
- Signals sinnvoll statt unnötig komplexer RxJS-Ketten für einfachen
  lokalen State? Umgekehrt: kein `subscribe()`-Missbrauch dort, wo ein
  `computed()`/Signal einfacher wäre?

## RxJS & Subscription-Leaks
- Jedes manuelle `.subscribe()` hat ein Gegenstück (`takeUntilDestroyed()`,
  `async` Pipe, `DestroyRef`)? Ein Leak entsteht typischerweise, wenn eine
  Component zerstört wird, während ein Observable (z. B. ein Interval oder
  ein HTTP-Polling) noch aktiv ist – das nicht nur an "gibt es ein
  `subscribe`" festmachen, sondern konkret prüfen, ob die Component vor dem
  Complete zerstört werden kann.
- `switchMap` vs. `mergeMap` vs. `concatMap` korrekt für den Use Case
  gewählt (z. B. `switchMap` bei Suche/Autocomplete, nicht `mergeMap`, sonst
  Race Conditions mit veralteten Ergebnissen)?

## Change Detection & Performance
- `OnPush` möglich, aber nicht gesetzt, obwohl die Component reine
  Input-Props ohne mutierten State hat?
- Teure Berechnungen direkt im Template (Methodenaufruf statt `computed()`/
  Pipe mit Memoization) – re-evaluiert bei jedem Change-Detection-Zyklus?

## i18n
- Sichtbare Strings hardcodiert in Template/TS statt über i18n-Mechanismus
  (`CLAUDE.md`: strikt mehrsprachig DE/EN, keine hardcodierten sichtbaren
  Texte)? Betrifft auch Fehlermeldungen, Placeholder, ARIA-Labels.

## Barrierefreiheit (a11y/ARIA)
- Interaktive Elemente über native Tags (`<button>`, `<a>`) statt `<div
  (click)>` ohne Tastatur-Unterstützung?
- Formularelemente mit zugehörigem `<label>`/`aria-label`?
- Fokus-Management bei dynamisch eingeblendeten Dialogen/Modals (Fokus wird
  hineingesetzt und beim Schließen zurückgegeben)?
- Ausreichender Farbkontrast bei DaisyUI-Theme-Anpassungen (keine reine
  Farbcodierung ohne Text/Icon-Alternative für Statusinformationen)?

## Typsicherheit
- `any` an Stellen, wo ein konkreter Typ/Interface möglich wäre – besonders
  an API-Response-Grenzen (führt zu stillen Runtime-Fehlern statt
  Compile-Fehlern)?
- Non-null-Assertion (`!`) ohne echte Garantie, dass der Wert vorhanden ist?

## Security (Frontend-spezifisch, ergänzt security-checklist.md)
- `[innerHTML]` mit dynamischem Inhalt ohne Sanitizing/DomSanitizer-Review?
- Tokens/Secrets im `localStorage`/`sessionStorage` statt HttpOnly-Cookie
  (XSS-Exposition)?
- Route Guards vorhanden für geschützte Routen – und deckungsgleich mit den
  serverseitigen `[Authorize]`-Regeln (kein Auseinanderlaufen von Frontend-
  und Backend-Berechtigungen)?

## Tests
- Test im vorgeschriebenen `Tests`-Ordner (`CLAUDE.md`)?
- TDD erkennbar (Test deckt das eigentlich erwartete Verhalten ab, nicht nur
  den zufällig aktuellen Implementierungsstand)?
