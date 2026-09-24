# ADR 0005: Volltextsuche mit SQL Server Full-Text Search (und Entscheidung D1: SQL Server)

- **Status:** Akzeptiert
- **Datum:** 2026-09-24
- **Kontext-Referenz:** Epic E-01 (Entscheidung D1), Story S13 (#17), Task S13-T1 (#80)

## Kontext

Zwei Fragen sind hier zu klären, weil S13 direkt von der ersten abhängt:

1. **D1 (Datenbank):** Der Code nutzt durchgehend SQL Server (`AddSqlServer`, `UseSqlServer`, `Testcontainers.MsSql` in allen Integrationstests seit S01–S12), `CLAUDE.md` nennt PostgreSQL als Standard-Stack. Diese Abweichung ist im Epic-Dokument seit S01 als offene Frage D1 vermerkt und wurde bislang nicht entschieden, weil keine Story sie zwingend brauchte — S13 (Volltextsuche) ist die erste, die eine explizite Wahl verlangt.
2. **S13-T1:** Welche Suchtechnologie für die Volltextsuche über `AudioJobs.RawTranscript`, `TranscriptSegments.Text` und `TranscriptVariants.Text` (S10; hat `AudioJob.ProcessedTranscript` aus der ursprünglichen Spec-Formulierung ersetzt)?

## Entscheidung D1: SQL Server

**Bei SQL Server bleiben**, nicht auf PostgreSQL migrieren.

Begründung:
- Der gesamte bestehende Code (12 Stories, alle EF-Core-Migrationen, `Testcontainers.MsSql`-Fixtures in praktisch jeder Integrationstest-Klasse, Aspire- und docker-compose-Konfiguration) ist bereits auf SQL Server aufgebaut. Eine Migration auf PostgreSQL wäre ein eigener, große Umbau (jede Migration neu, jede Testcontainers-Fixture, `AppDbContext`-Konfigurationen, die `ILogger`/Rowversion/Concurrency-Muster) und stünde in keinem Verhältnis zum Nutzen dieser Story.
- `CLAUDE.md` nennt PostgreSQL, aber die Datei ist projektweite Richtlinie, kein unveränderliches Gesetz — genau für solche Zielkonflikte sieht der Prozess ADRs vor. Diese ADR dokumentiert die bewusste Abweichung.
- Der Product Owner hat diese Abweichung ausdrücklich bestätigt (siehe Sitzungsprotokoll dieser Story).

**Konsequenz für `CLAUDE.md`:** Der Abschnitt „🚀 Tech Stack“ nennt weiterhin PostgreSQL; diese ADR gilt als dokumentierte, projektspezifische Ausnahme davon. Eine spätere Anpassung von `CLAUDE.md` selbst liegt außerhalb des Scopes dieser Story.

## Entscheidung S13-T1: SQL Server Full-Text Search

**SQL Server Full-Text Search** (`CREATE FULLTEXT CATALOG`/`CREATE FULLTEXT INDEX`, Abfrage über `CONTAINS`) gegenüber semantischer Suche über Qdrant.

### Bewertung

| Kriterium | SQL Server Full-Text Search | Qdrant (semantische Suche) |
|---|---|---|
| Zusätzliche Infrastruktur | Keine — Teil der ohnehin vorhandenen Datenbank | Neuer Service (Vektor-DB) plus Embedding-Erzeugung (Ollama/OpenAI) für jedes Transkript und jede Suchanfrage |
| Stemming/gebeugte Formen (DoD-Kriterium) | Ja, über Sprachkonfiguration (`LANGUAGE German`/`English` pro Spalte); SQL Server erkennt automatisch die passende Wortstamm-Form | Ja, aber implizit über Embedding-Ähnlichkeit, kein exaktes Stemming-Verhalten |
| Exakte Treffer/Snippets mit Position | Ja, `CONTAINS`/`FREETEXTTABLE` liefern Rang, Positionen selbst müssen aber in der Anwendung berechnet werden (kein natives Highlighting) | Nein direkt — semantische Treffer haben keine Wort-Position im Text |
| Implementierungsaufwand | Gering: Migration mit rohem SQL für Katalog/Index, `CONTAINS` in einer parametrisierten Abfrage | Hoch: Ollama/OpenAI-Client für Embeddings, Qdrant-Client, Synchronisierung bei jeder Transkript-Änderung, zusätzlicher Fehlerkanal |
| Passt zum Scope der Story | Ja — Story verlangt Volltextsuche mit Fundstellen, kein „ähnliche Bedeutung finden“ | Übererfüllt den Scope (Gold-Plating), eigene Story wäre angemessener (vgl. Tech-Stack-Hinweis in der Story-Beschreibung) |
| Betrieb (Aspire/docker-compose) | Bereits vorhandener SQL-Server-Container; **geprüft:** die Full-Text-Search-Komponente ist im offiziellen Linux-Container-Image `mcr.microsoft.com/mssql/server:2022-latest` seit SQL Server 2019 standardmäßig enthalten (kein separates Feature-Install nötig, anders als unter Windows) — bestätigt durch `CREATE FULLTEXT CATALOG`/`CREATE FULLTEXT INDEX` im selben Testcontainers-Image, das auch `DatabaseMigrationTests` verwendet | Neuer Container/Service in beiden Umgebungen |

### Entscheidung

SQL Server Full-Text Search. Geringster Aufwand, keine neue Infrastruktur, erfüllt das DoD-Kriterium „gebeugte Form (Stemming)“ direkt über die Sprachkonfiguration, und bleibt beim tatsächlichen Scope der Story (Volltext-, nicht semantische Suche). Qdrant ist laut Tech-Stack-Hinweis in der Story-Beschreibung explizit als **eigene, spätere** Story vorgesehen, sollte semantische Suche gewünscht sein.

### Umfang der Volltextsuche

Die ursprüngliche Formulierung „`RawTranscript`, `ProcessedTranscript` und Segmente“ bezieht sich auf den Datenstand vor S10; `AudioJob.ProcessedTranscript` wurde in S10 durch `TranscriptVariant.Text` ersetzt. Die Volltextsuche deckt entsprechend ab:
- `AudioJobs.RawTranscript`
- `TranscriptSegments.Text` (liefert `segmentStartMs` für den Sprung in den Player, S06)
- `TranscriptVariants.Text` (die S10-Nachfolge von `ProcessedTranscript`)

Jede Tabelle braucht für `CREATE FULLTEXT INDEX` einen eindeutigen Einzelspalten-Index; alle drei Tabellen haben bereits einen GUID-Primärschlüssel, der dafür genutzt wird.

## Konsequenzen

- **Escaping/Injection:** Nutzereingaben werden nicht direkt in FTS-Syntax eingebettet. Der gesamte Suchbegriff wird als eine einzige, in Anführungszeichen gesetzte Phrase an `CONTAINS` übergeben (innere Anführungszeichen werden verdoppelt), wodurch FTS-Operatoren (`AND`, `NOR`, `*`, `"`) in der Nutzereingabe als reiner Text behandelt werden statt als Suchsyntax. Der Parameterwert selbst ist über EF Core parametrisiert (kein String-Concatenation-SQL).
- **Snippets/Hervorhebung:** SQL Server liefert keine Wortposition im Text. Snippet-Fenster und Hervorhebungs-Offsets (Start/Länge) werden serverseitig in C# berechnet (Suche nach dem ersten Wort der Anfrage im geladenen Text, Fenster von ±80 Zeichen), nicht als HTML — das Frontend rendert die Hervorhebung selbst per `<mark>` anhand der gelieferten Offsets.
- **Migrationen:** `CREATE FULLTEXT CATALOG`/`CREATE FULLTEXT INDEX` sind kein EF-Core-Modellkonzept und werden über rohes SQL in einer Migration erstellt (`migrationBuilder.Sql(...)`), mit einem passenden `Down()`, das Index und Katalog wieder entfernt.
- **Sprachkonfiguration:** Die Volltextindizes nutzen `LANGUAGE German` (1031), da die Kernzielgruppe deutsche Aufnahmen transkribiert (siehe `Whisper:SupportedLanguages`, `de` an erster Stelle); das deckt das DoD-Kriterium „gebeugte Form“ für deutsche Suchbegriffe ab. Englische Umlaute/Stemming ist über dieselbe Sprachkonfiguration nur eingeschränkt korrekt; das ist eine bewusste Vereinfachung (ein Suchindex, keine Sprach-Erkennung pro Job).
