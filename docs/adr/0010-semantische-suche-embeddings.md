# ADR 0010: Semantische Suche – Embedding-Modell, Chunking, Qdrant und Hybrid-Suche (D7)

- **Status:** Akzeptiert
- **Datum:** 2026-11-10
- **Kontext-Referenz:** Epic E-02, Story S23 (#138), Task S23-T1, Enabler EN-3 (Qdrant) und EN-4 (`IEmbeddingService`), Entscheidung D7

## Kontext

Die Volltextsuche (S13, [ADR 0005](0005-volltextsuche.md)) findet Wörter und ihre Beugungen, aber keine Bedeutungen. ADR 0005 hat Qdrant bewusst als eigene Story ausgeklammert; S23 holt das nach, S24 (Chat mit Transkripten) baut darauf auf. Das Datenschutzversprechen „offline“ bleibt Standard: Transkriptinhalte verlassen den Rechner nur, wenn jemand einen externen Anbieter ausdrücklich konfiguriert.

## Entscheidungen

### 1. Embedding-Modell (D7): lokal als Standard, OpenAI nur per Opt-in

- **Standard:** `bge-m3` über Ollama (1024 Dimensionen, mehrsprachig, gut für Deutsch). Das Modell wird im Aspire-AppHost neben `llama3.2` bereitgestellt; ohne Aspire gilt der Ollama-Endpunkt aus der Konfiguration.
- **Opt-in:** Provider `OpenAI` (auch für LM Studio und andere OpenAI-kompatible Server über einen eigenen Endpunkt). Standardmodell dafür `text-embedding-3-small` mit `dimensions=1024`, damit die Dimension gleich bleibt. Der Schlüssel kommt nur aus Environment Variable oder Secret Manager, nie aus dem Code.
- **Abstraktion (EN-4):** `IEmbeddingService` (`EmbedAsync(texts)`, `Info` mit Provider, Modell und Dimension) kapselt einen `IEmbeddingGenerator<string, Embedding<float>>` (Microsoft.Extensions.AI) und ergänzt Polly-Retry bei vorübergehenden Fehlern, Batching und die Prüfung der gelieferten Dimension. Jede Konfiguration über `Embedding:*`; ohne Konfiguration ist die semantische Suche aus (503, in der UI ausgeblendet).

### 2. Distanzmetrik und Dimension

Cosine-Distanz, 1024 Dimensionen. Die Vektoren werden normalisiert abgelegt, Qdrant rechnet mit Cosine.

### 3. Umgang mit Modellwechsel (Re-Indexing)

Vektoren verschiedener Modelle sind nicht vergleichbar. In der Tabelle `EmbeddingIndexStates` steht, mit welchem Provider, Modell und welcher Dimension der Index gebaut wurde. Weicht die Konfiguration davon ab, gilt der Index als veraltet: Suche und Indexierung liefern `409`/pausieren, bis ein Admin den Neuaufbau (`POST /api/admin/search/reindex`) auslöst. Der Neuaufbau löscht die Collection, legt sie mit der neuen Dimension an und indexiert alle abgeschlossenen Jobs neu; der Fortschritt steht im Log. Der Neuaufbau ist auch der Weg, bestehende Daten erstmals zu indexieren.

### 4. Chunking

Segmente (mit der korrigierten Fassung aus S32) werden zu überlappenden Abschnitten von rund 45 Sekunden mit 15 Sekunden Überlappung und höchstens 1200 Zeichen zusammengefasst, an Segmentgrenzen. Jeder Chunk hat eine feste, aus Job und Position abgeleitete Id, damit Neuindexierung idempotent ist (überschreibt statt zu verdoppeln). Das Feld `variant` im Payload ist vorerst immer `raw`; Varianten (S10) werden nicht indexiert.

### 5. Qdrant-Layout und Berechtigungen

Collection `transcript_chunks`, Payload `jobId`, `ownerId`, `segmentStartMs`, `segmentEndMs`, `variant`; der Text liegt ebenfalls im Payload (für Snippets und S24), taucht aber in keinem Log auf. Die Suche filtert **serverseitig auf die Job-Ids, die der Benutzer sehen darf** (eigene und freigegebene, [ADR 0007](0007-jobs-pro-benutzer.md)), die aus der Datenbank berechnet werden. `ownerId` im Payload ist zusätzliche Information, nicht die Berechtigungsgrundlage: Freigaben ändern sich, der Payload müsste sonst bei jeder Änderung nachgezogen werden.

### 6. Hybrid-Suche

`mode=hybrid` kombiniert die Rangliste der Volltextsuche (S13) und der Vektorsuche per Reciprocal Rank Fusion (`score = Σ 1 / (60 + rank)`). Ein Volltexttreffer (Job, Zeit) und ein semantischer Treffer (Job, Zeitbereich) gelten als derselbe Treffer, wenn die Zeit im Bereich liegt.

### 7. Indexierung und Löschen

Nach Abschluss eines Jobs und nach jeder Segmentkorrektur (S32) wird der Job in eine Warteschlange gestellt; ein Hintergrunddienst erzeugt die Chunks, die Embeddings und schreibt sie nach Qdrant. Schlägt das fehl, wird protokolliert und der Job bleibt unberührt (kein Blockieren der Transkription). Beim Löschen eines Jobs (S09) und durch die Aufbewahrungsfrist (S30) werden dessen Vektoren entfernt (Best Effort mit Log; verwaiste Vektoren sind wegen des Berechtigungsfilters nicht abrufbar und verschwinden beim Neuaufbau).

## Konsequenzen

- Neue Abhängigkeiten: Qdrant als Container (Aspire-AppHost und docker-compose, Volume, Health Check), `Qdrant.Client`, Ollama-Modell `bge-m3`, optional OpenAI-Anbindung.
- Das Evaluationsset mit Synonymen (DoD) braucht ein echtes Modell und läuft deshalb nur, wenn ein Ollama-Endpunkt per Umgebungsvariable angegeben ist; die CI nutzt Fake-Embeddings.
- Offen: Varianten und Kapitel werden nicht indexiert; Re-Ranking und Mehrsprachigkeit der Anfrage sind nicht Teil dieser Story.
