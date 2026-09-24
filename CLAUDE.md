# AI Team – Projektregeln für Claude Code

Dieses Projekt ist eine agentengesteuerte Entwicklungsumgebung, basierend auf agilen Scrum-Prozessen, Domain-Driven Design (DDD) und Clean Architecture. Diese Datei wird bei jeder Session automatisch geladen und gilt für Claude und alle Subagenten in `.claude/agents/`.

## 🚀 Tech Stack
- **Backend:** .NET Core 10, C# 14, Wolverine
- **Frontend:** Angular 22, TypeScript 6, DaisyUI 5, i18n (DE/EN), ARIA (Accessibility)
- **Datenbanken:** PostgreSQL, Qdrant (Vektor-Datenbank)
- **Infrastruktur & Hosting:** .NET Aspire, Docker
- **Sicherheit & Auth:** Microsoft Identity, OWASP Guidelines
- **Künstliche Intelligenz:** OpenAI API, LMStudio, RAG (Retrieval-Augmented Generation)

## 🤖 Das Agenten-Team

Für jede Rolle existiert ein Subagent unter `.claude/agents/` (per Task-Tool aufrufbar) sowie ein passender Skill unter `.claude/skills/` mit Details, Vorgehen und Templates. Bei fachspezifischen Aufgaben den jeweiligen Skill/Subagenten priorisieren:

1. **`po-agent`** (Product Owner) – erstellt/verfeinert User Stories in `UserStories/` als `*.openspec.md`.
2. **`sm-agent`** (Scrum Master) – moderiert Dailys, löst Blocker, übernimmt Sprintplanung.
3. **`architect-agent`** (Tech Lead) – überwacht Clean Architecture/Vertical Slices, schreibt ADRs, High-Level Reviews.
4. **`frontend-agent`** – Angular 22 Standalone Components mit DaisyUI, TDD.
5. **`backend-agent`** – APIs/Domain-Logik (.NET 10, C# 14, PostgreSQL), TDD, Clean Architecture/Vertical Slices.
6. **`ai-agent`** (KI & Data Engineer) – LLM-Integrationen, RAG-Pipelines, Vector-Embeddings mit Qdrant.
7. **`qa-agent`** – E2E-, Last- und API-Compliance-Tests.
8. **`devops-agent`** – .NET Aspire, Docker, CI/CD, Observability.
9. **`sec-agent`** – Authentifizierung, RBAC (Microsoft Identity), API-/Frontend-Absicherung.
10. **`ux-agent`** – WCAG-Konformität (ARIA), semantisches HTML, Interaction-Design.
11. **`doc-agent`** – Swagger/OpenAPI-Doks, READMEs, Systemübersichten.

**Rollenklarheit:** Jeder Agent agiert strikt in seinem Fachbereich. Aufgaben, die den eigenen Fachbereich überschreiten, werden klar an den entsprechenden Agenten oder den User zurückgegeben.

## 📂 Projektstruktur
```text
.
├── .claude/
│   ├── agents/      # Subagenten-Definitionen (eine Rolle je Datei)
│   └── skills/      # Skills mit Details/Vorgehen/Templates je Rolle
├── UserStories/      # Zentrale Ablage für alle *.openspec.md Features & Stories
├── Tests/            # Zentraler Ordner für alle Frontend- und Backend-Tests
├── docs/adr/          # Architecture Decision Records
├── LICENSE
└── README.md
```

## 📐 Architektur & Methodik
- **Agile & OpenSpec:** Alle Anforderungen müssen als `*.openspec.md` in `UserStories/` spezifiziert sein.
- **Clean Architecture & Vertical Slices:** Strikte Trennung von Domain, Application, Infrastructure und Presentation/Web.
  - **Domain Layer:** Keine Infrastruktur-Abhängigkeiten (kein EF Core, keine HTTP-Clients). Aggregate Roots, Entities, Value Objects, Domain Events.
  - **Application Layer:** Use Cases, CQRS (Commands & Queries), Repository-Interfaces.
  - **Infrastructure Layer:** Implementiert Application-Interfaces (PostgreSQL, externe APIs, Qdrant).
  - **Presentation/Web Layer:** Kommuniziert ausschließlich mit der Application-Schicht.
  - **ADRs:** Wichtige Architektur-Entscheidungen werden schriftlich in `docs/adr/` dokumentiert.
- **Domain-Driven Design (DDD):** Klare Aggregate Roots, Value Objects, Domain Events im Core-Layer.
- **Test-Driven Development (TDD):** Tests (Red-Green-Refactor) werden VOR der Implementierung geschrieben. **Alle Tests liegen zwingend im Ordner `Tests`.**

## 🛠 Arbeitsweise
- **OpenSpec-Treue:** Grundlage jeder Implementierung ist die jeweilige `*.openspec.md`. Kein "Gold Plating" (unabgesprochene Features).
- **Systematic Debugging:** Kein "Trial & Error". Bei Fehlern zuerst Logs analysieren und Ursache verstehen, bevor Code geändert wird.
- **Beweise vor Behauptungen:** Ein Fix ist erst ein Fix, wenn der zugehörige Test lokal erfolgreich läuft.
- **Kommunikation:** Präzise, direkt, professionell, ohne unnötige Floskeln. Bei Blockern konkrete Handlungsoptionen vorschlagen.

## 🔀 Git & Code Reviews
- **Feature Branches:** Für jedes Feature/Bugfix ein eigener Branch (`feature/name-des-features`, `bugfix/kurze-beschreibung`). Direkte Commits in `main`/`develop` sind untersagt.
- **Conventional Commits:** `feat:`, `fix:`, `docs:`, `style:`, `refactor:`, `test:`, `chore:` – kurze, präzise englische Beschreibungen (z.B. `feat(auth): add JWT validation`).
- **Pull Requests:** Jede Code-Änderung wird über einen PR gemerged; PR erfordert passing Tests (CI/CD).
- **Self-Verification:** Vor Erstellen eines PRs selbstständig verifizieren, dass Code fehlerfrei kompiliert und alle lokalen Tests bestehen.

## 🏷 Naming Conventions
- **Backend (C# 14 / .NET):** `PascalCase` für Klassen/Methoden/Interfaces/Properties (`IUserService`, `GetUser()`); `camelCase` für lokale Variablen/Parameter; `_camelCase` für private Felder.
- **Frontend (Angular 22 / TypeScript):** `kebab-case` für Dateinamen/Komponenten-Selektoren (`user-profile.component.ts`, `<app-user-profile>`); `PascalCase` für Klassen/Interfaces; `camelCase` für Methoden/Properties/Variablen.

## 🔒 Sicherheit
- **Keine Hardcoded Secrets:** Passwörter, API-Keys, Connection Strings niemals im Code. Über Environment Variables (`appsettings.json`, `.env`) oder Secret Manager laden.
- **Endpoint-Security:** Backend-Routen standardmäßig absichern (`[Authorize]`), außer explizit `[AllowAnonymous]`.

## 🗄 Datenbank & ORM (PostgreSQL & EF Core)
- Relationen explizit über `.Include()` laden, um N+1-Probleme zu vermeiden.
- Bestehende Migrationen nicht nachträglich editieren; bei Schemaänderungen neue Migration (`Add-Migration`) erstellen.

## 🧠 KI-Integration (OpenAI, LMStudio, Qdrant)
- **Resilienz & Rate-Limits:** Retry-Policy (z.B. Polly) oder robustes Error-Handling bei KI-API-Aufrufen.
- **Datenschutz:** Sensible Domänendaten nicht ungefiltert in externe Prompts einfließen lassen.

## ⚠️ Error Handling & Logging
- Zentrales Exception Handling über globale Middleware/Exception Filter statt Try-Catch in jedem Controller.
- Frontend zeigt nur saubere Fehlermeldungen; niemals Stacktraces oder interne DB-Fehler ausliefern. Leere `catch`-Blöcke sind untersagt.

## 🏗 Infrastruktur & Hosting
- **.NET Aspire** für lokale Orchestrierung (Backend, Datenbanken, KI-Services), Service-Discovery/DI nach `Aspire.Hosting`-Standard.
- **Docker:** Alle Services containerisierbar; externe Abhängigkeiten (PostgreSQL, Qdrant) lokal über Docker, idealerweise via Aspire AppHost orchestriert.
- Verknüpfung von Containern/Aspire-Ressourcen strikt über Environment Variables und Connection Strings.

## 🌐 Frontend-Spezifika
- **i18n:** Frontend strikt mehrsprachig (DE/EN); keine hardcodierten sichtbaren Text-Strings in HTML/TypeScript.
- **Barrierefreiheit (a11y/ARIA):** Semantische HTML-Tags, ARIA-Attribute, vollständige Tastatur-Navigierbarkeit.
