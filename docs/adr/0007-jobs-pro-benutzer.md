# ADR 0007: Jobs pro Benutzer mit Freigaben

- **Status:** Akzeptiert
- **Datum:** 2026-11-09
- **Entscheider:** Repo-Owner (D6), umgesetzt in Story S19 (#116)
- **Ersetzt:** Punkt 4 aus [ADR 0003](./0003-authentifizierung.md) („Gemeinsame Jobs“)

## Kontext

ADR 0003 hat bewusst gemeinsame Jobs für alle angemeldeten Benutzer festgelegt und eine spätere Umstellung über eine `OwnerId`-Spalte vorgesehen. Mit der Benutzerverwaltung (S18) gibt es mehrere Konten, und vertrauliche Aufnahmen dürfen nicht automatisch für alle sichtbar sein.

## Entscheidung

1. **Eigentümer:** Jeder Job hat einen Eigentümer (`AudioJobs.OwnerId`, Fremdschlüssel auf `AspNetUsers`). Der Upload setzt den angemeldeten Benutzer. **Standard ist privat.**
2. **Freigabe pro Job (D6):** Der Eigentümer gibt einen Job an einzelne Benutzer frei (`JobShares`, Schlüssel `AudioJobId` + `UserId`). Eine Freigabe bedeutet **nur Lesen**. Teams gibt es nicht.
3. **Rechte:**
   | Rolle am Job | Lesen (Detail, Segmente, Untertitel, Audio, Varianten, Sprecher, Suche, Live-Updates) | Ändern (Löschen, Abbrechen, Neu starten, Variante erzeugen, Sprecher umbenennen, Freigaben verwalten) |
   |---|---|---|
   | Eigentümer | ja | ja |
   | Freigegebener Benutzer | ja | nein, Antwort `403` |
   | Alle anderen (auch Admins) | nein, Antwort `404` | nein, Antwort `404` |
   Fremde Jobs antworten mit `404`, nicht `403`, damit die Existenz einer Job-ID nicht verraten wird.
4. **Admins sehen keine fremden Jobs.** Die Rolle `Admin` darf aber **Jobs ohne Eigentümer** (aus der Zeit vor S19) auflisten und einem Benutzer zuordnen (`/api/admin/jobs/...`).
5. **Altdaten:** Die Migration weist bestehende Jobs dem ersten Admin zu, sonst dem ersten Benutzer. Gibt es noch keinen Benutzer, bleibt `OwnerId` leer (nullable) und ein Admin ordnet später zu.
6. **Zentrale Zugriffsprüfung:** Ein Dienst (`JobAccessService`) beantwortet „welchen Zugriff hat Benutzer X auf Job Y“. Ein Endpoint-Filter (`JobAccessFilter`) wendet ihn auf alle Routen unter `/api/audio-jobs/{id}` an (Lesen für `GET`, Eigentümer für alles andere). Es gibt keine verteilten Einzelprüfungen in den Handlern.
7. **Live-Updates:** Der Hub sendet nicht mehr an alle (`Clients.All`), sondern über `IJobNotifier` an Eigentümer und Freigegebene (`Clients.Users(...)`, Benutzer-ID aus dem `NameIdentifier`-Claim). `JoinJobGroup` prüft den Lesezugriff.
8. **Suche und Liste:** Beide filtern serverseitig auf sichtbare Jobs (eigene und freigegebene).

## Verworfene Alternativen

- **Admins sehen alles:** Das widerspricht dem Ziel vertraulicher Aufnahmen. Für Altdaten reicht die Zuordnung von Jobs ohne Eigentümer.
- **Schreibrechte per Freigabe:** Erhöht die Komplexität (Konflikte, Löschen durch Dritte). Bei Bedarf später über eine `Permission`-Spalte erweiterbar.
- **Teams/Gruppen:** Erst sinnvoll, wenn Freigaben an Einzelne zu aufwendig werden.
- **Zugriffsprüfung als globaler EF-Query-Filter:** Der Filter bräuchte den aktuellen Benutzer im DbContext, und Hintergrunddienste ohne Benutzerkontext müssten ihn umgehen. Der explizite Dienst ist transparenter und besser testbar.
- **`403` statt `404` für fremde Jobs:** Würde die Existenz von Job-IDs bestätigen.

## Konsequenzen

- Neue Migration `AddJobOwnership` (Spalte, Tabelle `JobShares`, Backfill). Bestehende Migrationen bleiben unverändert.
- Alle Tests, die Jobs über die API ansprechen, brauchen einen Eigentümer (Test-Auth-Handler liefert einen festen Benutzer).
- Hintergrunddienste (Worker) lösen die Empfänger über `IJobNotifier` auf, nicht mehr über `IHubContext`.
- Mehrere API-Instanzen: `Clients.Users` braucht dann einen gemeinsamen SignalR-Backplane. Das gilt auch schon für das bisherige `Clients.All`.
- Frontend: Freigabe-Dialog und Kennzeichnung „Geteilt von …“ (eigene Aufgabe in S19).
