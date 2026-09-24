# Feature: [Feature Name]

## 1. Description
[Kurze Beschreibung des geschäftlichen Nutzens und des Features]

## 2. User Stories
- Als [Rolle] möchte ich [Aktion], damit [Nutzen].

## 3. UI/UX Requirements
- **Komponenten**: [Liste der DaisyUI/Angular Standalone Components]
- **State**: [Welcher State muss im Frontend verwaltet werden?]

## 4. API & Backend Requirements
- **Authentifizierung/Autorisierung**: [Benötigte Rollen/Claims via Microsoft Identity]
- **Endpoints**:
  - `GET /api/[resource]`: [Beschreibung]
  - `POST /api/[resource]`: [Beschreibung]
- **Modelle/Entitäten (PostgreSQL)**:
  - `[EntityName]`: [Felder, z.B. Id (Guid), Name (string)]

## 5. KI/AI Requirements (Optional)
- **Vektorsuche (Qdrant)**: [Ja/Nein]
- **Prompt/System Message**: [Falls relevant]

## 6. Acceptance Criteria (DoD)
- [ ] Backend API (C# 14) ist implementiert und getestet (xUnit).
- [ ] Angular 22 Komponente ist implementiert und gestylt (DaisyUI 5).
- [ ] E2E Tests (QA Agent) sind grün.
- [ ] Swagger/OpenAPI Compliance ist gegeben.
