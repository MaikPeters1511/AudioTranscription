using System.Text.Json;

namespace AudioTranscription.Domain.Chapters;

/// <summary>
/// Reads the chapters out of an LLM answer (S25-T1). Models wrap JSON in code fences or chatty text, so this
/// looks for the array itself, skips unusable entries and only gives up (<see cref="ChapterParseException"/>)
/// when nothing usable is left.
/// </summary>
public static class ChapterResponseParser
{
    public const int MaxTitleLength = 200;
    public const int MaxSummaryLength = 1_000;

    public static IReadOnlyList<ChapterDraft> Parse(string response, int minIndex, int maxIndex)
    {
        var array = FindArray(response)
            ?? throw new ChapterParseException("The answer of the language model contains no chapter list.");

        var drafts = new List<ChapterDraft>();
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
                continue;

            var start = Property(element, "startSegment");
            var title = Property(element, "title");
            var summary = Property(element, "summary");
            if (start is not { ValueKind: JsonValueKind.Number } || !start.Value.TryGetInt32(out var index))
                continue;
            if (index < minIndex || index > maxIndex)
                continue;
            if (title is not { ValueKind: JsonValueKind.String } || string.IsNullOrWhiteSpace(title.Value.GetString()))
                continue;

            drafts.Add(new ChapterDraft(
                index,
                Cut(title.Value.GetString()!.Trim(), MaxTitleLength),
                Cut(summary is { ValueKind: JsonValueKind.String } ? summary.Value.GetString()!.Trim() : string.Empty, MaxSummaryLength)));
        }

        return drafts.Count > 0
            ? drafts
            : throw new ChapterParseException("The answer of the language model contains no usable chapter.");
    }

    /// <summary>The first JSON array in the text, or the array under a "chapters" property of an object.</summary>
    private static JsonElement? FindArray(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return null;

        // Try the outermost object first (e.g. {"chapters": [...]}), then the outermost array
        foreach (var (open, close) in new[] { ('{', '}'), ('[', ']') })
        {
            var from = response.IndexOf(open);
            var to = response.LastIndexOf(close);
            if (from < 0 || to <= from)
                continue;

            try
            {
                using var document = JsonDocument.Parse(response[from..(to + 1)]);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Array)
                    return root.Clone();
                if (root.ValueKind == JsonValueKind.Object && Property(root, "chapters") is { ValueKind: JsonValueKind.Array } chapters)
                    return chapters.Clone();
            }
            catch (JsonException)
            {
                // Not valid JSON in this span; the other bracket kind may still work
            }
        }
        return null;
    }

    private static JsonElement? Property(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }
        return null;
    }

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..max].TrimEnd();
}
