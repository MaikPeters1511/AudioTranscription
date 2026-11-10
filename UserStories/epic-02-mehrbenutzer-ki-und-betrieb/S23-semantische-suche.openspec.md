# Story S23: Semantische Suche mit Embeddings (Qdrant)

> Epic: [E-02](./00-epic.openspec.md) · Phase 3 · Agenten: architect-agent, ai-agent, backend-agent, frontend-agent · Größe: L · Voraussetzung: EN-3, EN-4, Entscheidung D7

## 1. Description
Die Volltextsuche (S13, ADR 0005) findet Wörter und ihre Beugungen, aber keine Bedeutungen. Semantische Suche über Embeddings in Qdrant findet auch „Budgetrunde“ bei der Frage „Wann ging es ums Geld?“. ADR 0005 hat Qdrant bewusst als eigene Story ausgeklammert.

## 2. User Stories
- Als Benutzer möchte ich in natürlicher Sprache suchen, damit ich Aussagen auch ohne das genaue Wort finde.

## 3. API & Backend Requirements
- **Qdrant:** Collection `transcript_chunks` mit Payload (`jobId`, `ownerId`, `segmentStartMs`, `segmentEndMs`, `variant`), kein Volltext in Logs.
- **Chunking:** Segmente zu überlappenden Abschnitten (ca. 30–60 s) zusammenfassen.
- **Endpoints:** `GET /api/search/semantic?q=&page=&pageSize=`; optional `mode=hybrid` (Kombination mit FTS per Reciprocal Rank Fusion).
- **Indexierung:** Hintergrunddienst/Wolverine-Handler nach Abschluss eines Jobs; Neuaufbau-Kommando für bestehende Daten.

## 4. Tasks

### S23-T1 ADR: Embedding-Modell, Chunking, Hybrid-Suche · architect, ai · S
- **AC:** ADR in `docs/adr/` (D7): lokales Modell als Standard, Dimension, Distanzmetrik, Umgang mit Modellwechsel (Re-Indexing), Datenschutz.

### S23-T2 Infrastruktur: Qdrant (EN-3) · devops · S
- **AC:** Qdrant-Container im Aspire-AppHost und docker-compose, Connection String per Environment Variable, Health Check, Volume.

### S23-T3 Indexierung · ai, backend · M
- **AC (TDD):**
  - [ ] Tests mit Fake-Embedding und Qdrant-Testcontainer: Chunks werden erzeugt, upserted und bei Job-Löschung (S09/S30) entfernt.
  - [ ] Retry-Policy bei Embedding-Aufrufen (Polly), fehlgeschlagene Indexierung blockiert den Job nicht.

### S23-T4 Such-Endpoint mit Berechtigungsfilter · backend, sec · M
- **AC (TDD):** Filter auf `ownerId`/Freigaben (S19) ist serverseitig erzwungen, Test mit zwei Benutzern; Treffer enthalten Sprungmarke.

### S23-T5 Frontend: Modus-Umschalter in der Suche · frontend · S
- **AC:** Umschalter „Wörtlich / Bedeutung / Kombiniert“, Treffer springen im Player, DE/EN, ARIA.

### S23-T6 Re-Indexing-Kommando · backend · S
- **AC:** Admin-Endpoint oder CLI-Befehl baut den Index neu auf, mit Fortschrittsanzeige im Log.

## 5. KI/AI Requirements
- **Vektorsuche (Qdrant):** Ja.
- Standard: lokales Embedding-Modell, OpenAI nur per Opt-in. Keine Transkript-Inhalte an externe Dienste ohne Konfiguration.

## 6. Acceptance Criteria (DoD)
- [ ] Beispiel-Suchen mit Synonymen finden die erwarteten Stellen (kleines Evaluationsset im Testprojekt).
- [ ] Kein Benutzer sieht Treffer fremder Jobs, Swagger und README aktualisiert.

## 7. Umsetzung

- **Entscheidung (T1):** [ADR 0010](../../docs/adr/0010-semantische-suche-embeddings.md) (D7): `bge-m3` über Ollama (1024 Dimensionen, Cosine) als Standard, OpenAI nur per Opt-in (`Embedding:Provider=OpenAI` mit Schlüssel aus Environment/Secret), Chunks von rund 45 s mit 15 s Überlappung, Modellwechsel macht den Index veraltet bis zum Neuaufbau, Hybrid per Reciprocal Rank Fusion.
- **Infrastruktur (T2/EN-3):** Qdrant im Aspire-AppHost (Datenvolume, `bge-m3` neben `llama3.2`) und in `docker-compose.yml` (Volume, Health Check, Verbindung per Environment Variable), Readiness-Check `qdrant`. Ohne `qdrant`-Connection-String und ohne nutzbares Embedding-Modell ist die semantische Suche aus (503, Umschalter ausgeblendet).
- **Embeddings (EN-4):** `IEmbeddingService` über `IEmbeddingGenerator` (Microsoft.Extensions.AI): Batches, Polly-Retry, Dimensionsprüfung, normierte Vektoren.
- **Indexierung (T3):** `SemanticIndexer` + `IndexingWorker`: nach Abschluss eines Jobs und nach jeder Segmentkorrektur (S32) wird neu indexiert (feste Chunk-Ids, Neuindexierung überschreibt); Fehler werden protokolliert und blockieren den Job nie, ein fehlgeschlagener Lauf lässt die alten Chunks stehen. Löschen eines Jobs (S09) und Aufbewahrungsfrist (S30) entfernen dessen Vektoren (Best Effort). Qdrant-Verhalten ist gegen einen echten Container getestet (Testcontainers), die übrige Logik mit Fake-Embedding und In-Memory-Index.
- **Suche (T4):** `GET /api/search/semantic`; die erlaubten Job-Ids (eigene und freigegebene, S19) werden aus der Datenbank berechnet und als Filter in die Vektorsuche gegeben, die Berechtigung gilt also zum Suchzeitpunkt (Test mit zwei Benutzern und entzogener Freigabe). Treffer enthalten die Sprungmarke.
- **Frontend (T5):** Umschalter „Wörtlich / Bedeutung / Kombiniert“ (Radio-Gruppe mit Legende und Hinweis), nur sichtbar, wenn der Server die Modi anbietet; Tag-/Ordnerfilter gelten nur für „Wörtlich“; DE/EN.
- **Neuaufbau (T6):** `POST /api/admin/search/reindex` und `GET /api/admin/search/status` (Fortschritt auch im Log).
- **Nicht umgesetzt/offen:** Das Evaluationsset mit Synonymen (`SemanticEvaluationTests`) läuft nur mit einem echten Modell (`EMBEDDING_EVAL_OLLAMA_URL`). Gemessen mit `bge-m3` (15 Passagen, 15 Fragen in anderen Worten): die passende Passage steht bei 13 von 15 Fragen im besten und bei 15 von 15 unter den drei besten Chunks (Details in ADR 0011). Das Set ist klein und künstlich, mit echten Transkripten ist die Qualität nicht gemessen. Varianten und Kapitel werden nicht indexiert, in der Hybrid-Suche zählt die Reihenfolge der Volltextsuche (neueste zuerst), nicht ein Relevanzwert. Die Vektorsuche liefert keine Gesamtzahl, die Paginierung weiß nur, ob eine weitere Seite folgt. Der OpenAI-Pfad ist ohne Schlüssel nicht getestet.
