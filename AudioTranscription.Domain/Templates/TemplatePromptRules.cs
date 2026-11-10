using System.Text.RegularExpressions;

namespace AudioTranscription.Domain.Templates;

/// <summary>What a template's prompt may contain (S26-T2) and how its placeholders are filled.</summary>
public static partial class TemplatePromptRules
{
    public const int MaxPromptLength = 4_000;
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 300;
    public const string Speakers = "SPEAKERS";
    public const string Language = "LANGUAGE";
    private static readonly HashSet<string> Known = [Speakers, Language];

    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}")]
    private static partial Regex PlaceholderPattern();

    public static IReadOnlyList<string> Validate(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return ["The prompt is required."];

        var errors = new List<string>();
        if (prompt.Length > MaxPromptLength)
            errors.Add($"The prompt must be at most {MaxPromptLength} characters long.");
        foreach (Match match in PlaceholderPattern().Matches(prompt))
        {
            if (!Known.Contains(match.Groups[1].Value))
                errors.Add($"The placeholder '{{{{{match.Groups[1].Value}}}}}' is not known. Use {string.Join(", ", Known.Select(k => $"{{{{{k}}}}}"))}.");
        }
        return errors;
    }

    /// <summary>{{SPEAKERS}} becomes the names of the named speakers, {{LANGUAGE}} stays a hint for the model.</summary>
    public static string Fill(string prompt, IReadOnlyCollection<string> namedSpeakers) =>
        PlaceholderPattern().Replace(prompt, match => match.Groups[1].Value switch
        {
            Speakers => namedSpeakers.Count > 0 ? string.Join(", ", namedSpeakers) : "(none named)",
            Language => "the language of the transcript",
            _ => match.Value,
        });
}
