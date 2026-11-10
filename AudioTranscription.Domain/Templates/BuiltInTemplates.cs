using AudioTranscription.Domain.Entities;

namespace AudioTranscription.Domain.Templates;

/// <summary>The templates every installation ships with (S26-T1). Their ids are fixed so they can be seeded by a migration.</summary>
public static class BuiltInTemplates
{
    public static readonly Guid MeetingMinutesId = new("5a1b0c10-0000-4000-8000-000000000001");
    public static readonly Guid DecisionListId = new("5a1b0c10-0000-4000-8000-000000000002");
    public static readonly Guid TasksWithOwnersId = new("5a1b0c10-0000-4000-8000-000000000003");

    private const string Rules =
        " Use only what is said in the transcript; do not invent facts, names or dates." +
        " Leave out a field when the transcript does not say anything about it." +
        " Write in {{LANGUAGE}}. Speakers named in the transcript: {{SPEAKERS}}." +
        " Name a person as owner only if they are one of these speakers.";

    public static IReadOnlyList<PostprocessingTemplate> All { get; } =
    [
        new()
        {
            Id = MeetingMinutesId,
            Name = "Besprechungsprotokoll",
            Description = "Zusammenfassung, Beschlüsse und Aufgaben einer Besprechung",
            Prompt = "Write minutes of the meeting: a short title, a summary of two to four sentences, the decisions that were made, " +
                     "and the tasks that were assigned, each with its owner and due date if they are mentioned." + Rules,
            OutputSchema = """
                {"type":"object","required":["summary"],"properties":{
                  "title":{"type":"string"},
                  "summary":{"type":"string","title":"Zusammenfassung"},
                  "decisions":{"type":"array","title":"Beschlüsse","items":{"type":"string"}},
                  "tasks":{"type":"array","title":"Aufgaben","items":{"type":"object","required":["task"],"properties":{
                    "task":{"type":"string"},"owner":{"type":"string","title":"Verantwortlich"},"due":{"type":"string","title":"Fällig"}}}}}}
                """,
            IsBuiltIn = true,
        },
        new()
        {
            Id = DecisionListId,
            Name = "Entscheidungsliste",
            Description = "Alle Beschlüsse mit Begründung",
            Prompt = "List every decision that was made in the conversation, each with a short reason if one is given." + Rules,
            OutputSchema = """
                {"type":"object","required":["decisions"],"properties":{
                  "decisions":{"type":"array","title":"Beschlüsse","items":{"type":"object","required":["decision"],"properties":{
                    "decision":{"type":"string"},"reason":{"type":"string","title":"Begründung"}}}}}}
                """,
            IsBuiltIn = true,
        },
        new()
        {
            Id = TasksWithOwnersId,
            Name = "Aufgaben mit Verantwortlichen",
            Description = "Aufgabenliste mit Verantwortlichen und Terminen",
            Prompt = "List every task that was assigned or agreed on, with the person responsible and the due date if they are mentioned." + Rules,
            OutputSchema = """
                {"type":"object","required":["tasks"],"properties":{
                  "tasks":{"type":"array","title":"Aufgaben","items":{"type":"object","required":["task"],"properties":{
                    "task":{"type":"string"},"owner":{"type":"string","title":"Verantwortlich"},"due":{"type":"string","title":"Fällig"}}}}}}
                """,
            IsBuiltIn = true,
        },
    ];
}
