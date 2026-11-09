# Epic E-02: Mehrbenutzer, KI-Ausbau & Betrieb

> GitHub: Epic #110, Stories #111–#198 (Story-Nummern in der Tabelle), Tasks als Sub-Issues verknüpft

## 1. Description
E-01 hat die Basis geschaffen: Upload, Aufnahme, Segmente, Sprechererkennung, Volltextsuche, Ollama-Nachbearbeitung, Authentifizierung und CI. E-02 baut darauf auf, in drei Richtungen:

- **Mehrbenutzer & Organisation:** Aktuell sehen alle angemeldeten Benutzer dieselben Jobs, es gibt keine Benutzerverwaltung und keine Möglichkeit, Transkripte zu ordnen.
- **KI-Ausbau:** Die Suche arbeitet nur lexikalisch. Der Tech-Stack nennt Qdrant und RAG, beides ist noch ungenutzt.
- **Robustheit & Betrieb:** Große Uploads, Aufbewahrungsfristen, Metriken und Kern-E2E-Tests fehlen.

**Ziel:** Nach Phase 1 steht ein tragfähiges Berechtigungs- und Betriebsfundament. Phase 2 verbessert Alltagsnutzen und Bedienung. Phase 3 liefert die größeren KI- und Integrationsstufen, jeweils nach ADR oder Spike.

## 2. Stories

### Phase 1: Fundament (Sprint 1–2)
| ID | Issue | Story | Agent(s) | Größe |
|----|-------|-------|----------|-------|
| [S31](./S31-e2e-kernflows.openspec.md) | #180 | E2E-Tests für die Kern-Flows (Sicherheitsnetz vor dem Umbau) | qa | M |
| [S18](./S18-benutzerverwaltung.openspec.md) | #111 | Benutzerverwaltung und Rollen (Admin-UI) | sec, backend, frontend | L |
| [S19](./S19-jobs-pro-benutzer.openspec.md) | #116 | Jobs pro Benutzer und Freigaben | architect, sec, backend, frontend | L |
| [S30](./S30-retention-policy.openspec.md) | #175 | Aufbewahrungsfristen (Retention) | backend, sec | M |
| [S29](./S29-observability.openspec.md) | #170 | Observability: Metriken, Traces, Dashboard | devops, backend | M |

### Phase 2: Alltagsnutzen (Sprint 3–4)
| ID | Issue | Story | Agent(s) | Größe |
|----|-------|-------|----------|-------|
| [S20](./S20-tags-ordner-favoriten.openspec.md) | #122 | Tags, Ordner und Favoriten | backend, frontend, ux | M |
| [S21](./S21-weitere-exportformate.openspec.md) | #127 | Weitere Exportformate (DOCX, Markdown, JSON, PDF) | backend, frontend | M |
| [S22](./S22-glossar.openspec.md) | #133 | Glossar für Fachbegriffe (Whisper-Initial-Prompt) | backend, frontend | M |
| [S34](./S34-batch-upload-und-queue.openspec.md) | #198 | Batch-Upload und Warteschlangen-Übersicht | backend, frontend, ux | M |
| [S27](./S27-resumable-upload.openspec.md) | #159 | Wiederaufnehmbarer Upload (Umsetzung von ADR 0006) | architect, backend, frontend | L |
| [S32](./S32-transkript-editor.openspec.md) | #187 | Transkript-Editor mit Korrektur und Versionierung | backend, frontend, ux | L |

### Phase 3: KI & Integration (ab Sprint 5, ADR/Spike zuerst)
| ID | Issue | Story | Agent(s) | Größe |
|----|-------|-------|----------|-------|
| [S23](./S23-semantische-suche.openspec.md) | #138 | Semantische Suche mit Embeddings (Qdrant) | architect, ai, backend, frontend | L |
| [S24](./S24-rag-chat.openspec.md) | #145 | Chat mit Transkripten (RAG) | ai, backend, frontend, sec | XL |
| [S25](./S25-kapitel-und-themen.openspec.md) | #151 | Automatische Kapitel und Themen | ai, backend, frontend | M |
| [S26](./S26-protokoll-vorlagen.openspec.md) | #155 | Meeting-Protokoll-Vorlagen | ai, backend, frontend | M |
| [S28](./S28-webhooks-api-keys-ordner-watch.openspec.md) | #164 | Webhooks, API-Keys und Ordner-Watch | architect, sec, backend | L |
| [S33](./S33-pwa-offline-aufnahme.openspec.md) | #192 | PWA und Offline-Aufnahme | frontend, ux | L |

**Abhängigkeiten:**
- S19 setzt S18 voraus, S20/S28/S30 setzen S19 (Besitzer pro Job) voraus.
- S23 ist Voraussetzung für S24. S25 und S26 sind unabhängig, nutzen aber die Muster aus S10.
- S27 setzt voraus, dass [ADR 0006](../../docs/adr/0006-resumable-upload-spike.md) eine Technologie festlegt.
- S32 ändert das Datenmodell der Segmente wie schon S04/S06: nicht parallel zu S25 umsetzen.

## 3. Übergreifende Voraussetzungen (Enabler)
- **EN-3 Qdrant im Aspire-AppHost und docker-compose:** Container, Connection String über Environment Variables, Health Check. Wird von S23 benötigt. *Agent: devops · Größe: S*
- **EN-4 Gemeinsame Embedding-Abstraktion (`IEmbeddingService`):** Austauschbar zwischen Ollama/LMStudio und OpenAI, mit Retry-Policy (Polly). Wird von S23–S26 benötigt. *Agent: ai · Größe: S*

## 4. Offene Entscheidungen (vor Sprint-Start klären)
| # | Frage | Betrifft | Empfehlung |
|---|-------|----------|------------|
| D6 | **Sichtbarkeitsmodell:** Privat pro Benutzer, geteilt pro Team oder beides? | S19, S20, S24 | Privat als Standard, explizite Freigabe pro Job an Benutzer. Teams später. |
| D7 | **Embedding-Modell und Dimension:** lokal (z. B. über Ollama) oder OpenAI? Datenschutzversprechen „offline“ beachten. | S23, S24 | Lokal als Standard, OpenAI nur per Opt-in-Konfiguration. |
| D8 | **PDF/DOCX-Erzeugung:** Welche Bibliothek, Lizenz prüfen. | S21 | Open-XML-SDK für DOCX, für PDF Lizenz (z. B. QuestPDF Community) prüfen. |
| D9 | **Upload-Protokoll:** tus oder eigenes Chunking? | S27 | Entscheidung im ADR 0006 nachziehen. |
| D10 | **Datenbank:** Der Tech-Stack nennt PostgreSQL, der Code nutzt SQL Server (siehe D1/ADR 0005). Gilt für alle neuen Migrationen weiter SQL Server? | alle mit Schema | Ja, wie in D1 entschieden. |

## 5. Acceptance Criteria (Epic-DoD)
- [ ] Alle Stories aus Phase 1 und 2 sind abgenommen, Phase-3-Stories haben mindestens ein abgeschlossenes ADR- oder Spike-Ergebnis.
- [ ] CI ist auf `main` grün, E2E-Kernflows (S31) laufen in der CI.
- [ ] Jeder Endpoint ist standardmäßig mit `[Authorize]`/`RequireAuthorization` geschützt, Zugriffe sind pro Benutzer geprüft (S19).
- [ ] Das Datenschutzversprechen „offline“ bleibt Standard: Externe KI-Dienste nur per ausdrücklicher Konfiguration.
- [ ] Alle neuen UI-Texte liegen in DE/EN vor, alle neuen Komponenten sind per Tastatur bedienbar und haben ARIA-Labels.
- [ ] Swagger/OpenAPI dokumentiert alle neuen Endpoints (`doc-agent`), README ist aktualisiert.
