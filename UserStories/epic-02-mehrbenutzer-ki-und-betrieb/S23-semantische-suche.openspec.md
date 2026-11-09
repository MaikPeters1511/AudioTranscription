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
