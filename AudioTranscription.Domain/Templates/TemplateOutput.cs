using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AudioTranscription.Domain.Templates;

/// <summary>The answer of the model for a template could not be turned into a valid document.</summary>
public class TemplateOutputException(string message) : Exception(message);

/// <summary>Works on the structured answer of a template (S26): finding it, filtering owners, rendering Markdown.</summary>
public static class TemplateOutput
{
    /// <summary>The outermost JSON object (or array) of an answer, tolerating code fences and chatty text; null if there is none.</summary>
    public static JsonNode? ExtractJson(string answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
            return null;

        foreach (var (open, close) in new[] { ('{', '}'), ('[', ']') })
        {
            var from = answer.IndexOf(open);
            var to = answer.LastIndexOf(close);
            if (from < 0 || to <= from)
                continue;
            try
            {
                return JsonNode.Parse(answer[from..(to + 1)]);
            }
            catch (JsonException)
            {
                // Not valid JSON in this span; the other bracket kind may still work
            }
        }
        return null;
    }

    /// <summary>
    /// Keeps an <c>owner</c> only if it is one of the named speakers (S11), written the way the speaker is named;
    /// every other owner is removed, so nobody is made responsible for a task by a guess of the model.
    /// </summary>
    public static void KeepOwners(JsonNode? node, IReadOnlyCollection<string> namedSpeakers)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj.ContainsKey("owner"))
                {
                    var owner = obj["owner"] is JsonValue v && v.TryGetValue<string>(out var text) ? text.Trim() : null;
                    var match = owner is null ? null : namedSpeakers.FirstOrDefault(n => n.Equals(owner, StringComparison.OrdinalIgnoreCase));
                    if (match is null)
                        obj.Remove("owner");
                    else
                        obj["owner"] = match;
                }
                foreach (var child in obj.Select(p => p.Value).ToList())
                    KeepOwners(child, namedSpeakers);
                break;
            case JsonArray array:
                foreach (var child in array)
                    KeepOwners(child, namedSpeakers);
                break;
        }
    }

    /// <summary>Renders a document as Markdown, in the order of the schema's properties; empty values are left out.</summary>
    public static string ToMarkdown(string schema, JsonNode document)
    {
        var markdown = new StringBuilder();
        if (JsonNode.Parse(schema) is not JsonObject root || document is not JsonObject obj || root["properties"] is not JsonObject properties)
            return string.Empty;

        foreach (var (name, propertySchema) in properties)
        {
            var heading = Heading(name, propertySchema);
            switch (obj[name])
            {
                case JsonValue value when value.TryGetValue<string>(out var text):
                    if (string.IsNullOrWhiteSpace(text))
                        break;
                    if (name == "title")
                        Append(markdown, $"# {text.Trim()}");
                    else
                        Append(markdown, $"## {heading}\n\n{text.Trim()}");
                    break;
                case JsonArray array:
                    var lines = array.Select(item => Item(item, propertySchema?["items"])).Where(l => l.Length > 0).ToList();
                    if (lines.Count > 0)
                        Append(markdown, $"## {heading}\n\n{string.Join("\n", lines.Select(l => $"- {l}"))}");
                    break;
            }
        }

        return markdown.Length == 0 ? string.Empty : markdown.ToString().TrimEnd('\n') + "\n";
    }

    private static string Item(JsonNode? item, JsonNode? itemSchema)
    {
        if (item is JsonValue plain)
            return plain.TryGetValue<string>(out var text) ? text.Trim() : plain.ToString();
        if (item is not JsonObject obj || itemSchema?["properties"] is not JsonObject properties)
            return string.Empty;

        // The first property is the item itself, the others are labelled details: "Task — Owner: Anna — Due: ..."
        var parts = new List<string>();
        var first = true;
        foreach (var (name, propertySchema) in properties)
        {
            var text = obj[name] is JsonValue v ? (v.TryGetValue<string>(out var s) ? s.Trim() : v.ToString()) : null;
            if (string.IsNullOrEmpty(text))
            {
                first = false;
                continue;
            }
            parts.Add(first ? text : $"{Heading(name, propertySchema)}: {text}");
            first = false;
        }
        return string.Join(" — ", parts);
    }

    private static string Heading(string name, JsonNode? schema) =>
        schema?["title"] is JsonValue title && title.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : char.ToUpperInvariant(name[0]) + name[1..];

    private static void Append(StringBuilder markdown, string block)
    {
        if (markdown.Length > 0)
            markdown.Append("\n\n");
        markdown.Append(block);
    }
}
