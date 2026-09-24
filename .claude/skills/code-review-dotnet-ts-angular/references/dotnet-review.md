# .NET / C# 14 Review-Checkliste

Bezug: `CLAUDE.md` (.NET Core 10, C# 14, Wolverine, Clean Architecture,
Vertical Slices, DDD, TDD, PostgreSQL/EF Core).

## Architektur & Schichtentrennung
- **Domain Layer** frei von Infrastruktur-Abhängigkeiten? Kein `using
  Microsoft.EntityFrameworkCore` oder HTTP-Client-Referenz in Entities/Value
  Objects/Domain Events.
- **Application Layer**: Use Cases/Commands/Queries sprechen nur über
  Repository-*Interfaces*, nicht gegen konkrete Infrastruktur-Klassen.
- **Presentation/Web Layer** kommuniziert ausschließlich mit der Application-
  Schicht – kein direkter Repository- oder DbContext-Zugriff aus einem
  Controller/Endpoint.
- Vertical-Slice-Grenzen eingehalten (Feature-Ordner statt technischer
  Schichten-Ordner querbeet)?
- Wichtige Architekturentscheidung ohne zugehöriges ADR in `docs/adr/`? (Nur
  melden, nicht selbst nachtragen – gehört an `architect-agent`.)

## DDD
- Aggregate Roots kapseln ihre Invarianten (keine öffentlichen Setter, die
  einen inkonsistenten Zustand erlauben)?
- Value Objects wirklich unveränderlich und über Gleichheit (nicht Referenz)
  vergleichbar?
- Domain Events dort ausgelöst, wo die fachliche Änderung passiert, nicht
  nachträglich im Application-Handler zusammengebastelt?

## Async/Await & Concurrency
- `async void` außerhalb von Event-Handlern?
- Fehlendes `ConfigureAwait`/blockierendes `.Result`/`.Wait()` auf einem
  async Call (Deadlock-Potenzial, insb. in Bibliothekscode)?
- `CancellationToken` bei I/O-Aufrufen durchgereicht oder verschluckt?

## EF Core / PostgreSQL
- N+1-Problem: Navigation Properties in einer Schleife nachgeladen statt
  vorab per `.Include()`? (Explizit in `CLAUDE.md` gefordert.)
- Migration nachträglich editiert statt neuer `Add-Migration`? (Prüfbar,
  wenn Migrationshistorie im Diff sichtbar ist.)
- Tracking vs. `AsNoTracking()` sinnvoll gewählt (Read-Queries ohne
  Tracking)?

## Nullable Reference Types & Fehlerbehandlung
- `#nullable enable` konsistent, keine `!`-Unterdrückung ohne echten Beweis
  der Non-Null-Garantie?
- Zentrales Exception Handling über Middleware/Filter vorhanden, oder
  Try-Catch-Wildwuchs in Controllern? (`CLAUDE.md`: zentral gefordert.)
- Leere `catch`-Blöcke? → immer melden, unabhängig vom Kontext.

## Naming & Konventionen (`CLAUDE.md`)
- `PascalCase` für Klassen/Methoden/Interfaces/Properties, `camelCase` für
  lokale Variablen/Parameter, `_camelCase` für private Felder eingehalten?

## Wolverine-spezifisch
- Handler folgen dem Command/Query-Muster ohne versteckte Seiteneffekte in
  Query-Handlern?
- Messaging/Handler-Registrierung nicht versehentlich doppelt (führt zu
  doppelter Verarbeitung von Nachrichten)?

## Tests
- Test liegt im vorgeschriebenen `Tests`-Ordner (`CLAUDE.md`)?
- Wurde für einen Bugfix erkennbar zuerst ein failing Test geschrieben
  (TDD), oder wirkt der Test nachträglich an die Implementierung angepasst
  (z. B. Assertion passt exakt zum Bug statt zum erwarteten Verhalten)?
