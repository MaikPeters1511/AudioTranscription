# ADR 0014: LM Studio statt Ollama für lokale Sprachmodelle

- **Status:** Akzeptiert
- **Datum:** 2026-11-16
- **Kontext-Referenz:** ersetzt die Ollama-Anbindung aus [ADR 0010](0010-semantische-suche-embeddings.md) und [ADR 0011](0011-rag-chat-prompt-und-injection-schutz.md)

## Kontext

Nachbearbeitung (S10), Kapitel (S25), semantische Suche (S23) und Chat (S24) liefen über einen Ollama-Container, den der AppHost startete und mit Modellen versorgte. Die Entwicklung nutzt stattdessen LM Studio, das auf dem Rechner läuft (GPU/Metal) und eine OpenAI-kompatible API anbietet.

## Entscheidung

- Alle lokalen Modellaufrufe gehen über die OpenAI-kompatible API von LM Studio (`LmStudio:Endpoint`, Standard `http://localhost:1234/v1`) mit dem bereits genutzten `OpenAI`-Client und `Microsoft.Extensions.AI`. Ein Platzhalter-Key genügt, LM Studio prüft ihn nicht.
- LM Studio läuft **nicht** als Container und wird nicht vom AppHost verwaltet: Modelle lädt und startet der Entwickler selbst in LM Studio. Der AppHost reicht nur Endpoint und Modellnamen durch (`LmStudio:Model`, `LmStudio:EmbeddingModel`).
- Provider-Werte für Chat und Embeddings: `LmStudio` (Standard, lokal) und `OpenAI` (extern, nur mit Key); `External` im Chat bleibt für alles außer `LmStudio` wahr.
- Ohne `LmStudio:Endpoint` bleiben Nachbearbeitung, Chat und semantische Suche aus; der Health-Check `lm-studio` meldet *Degraded*, wenn der Server nicht antwortet.
- Ollama wird vollständig entfernt (Container, Aspire-Paket, `Microsoft.Extensions.AI.Ollama`, Config `Features:OllamaPostProcessing`).

## Folgen

- Die Modellnamen hängen vom Katalog in LM Studio ab und müssen dort exakt stimmen; die Defaults (`llama-3.2-3b-instruct`, `text-embedding-bge-m3`) sind Platzhalter.
- Embeddings anderer Modelle sind nicht kompatibel: Nach einem Wechsel des Embedding-Modells muss der Index neu aufgebaut werden (`POST /api/admin/search/reindex`).
- Im Docker-Betrieb erreicht der Container LM Studio über `host.docker.internal`.
