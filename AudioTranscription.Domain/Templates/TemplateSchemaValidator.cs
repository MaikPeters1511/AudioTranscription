using System.Text.Json;
using System.Text.Json.Nodes;

namespace AudioTranscription.Domain.Templates;

/// <summary>
/// Checks the structured output of a template against its output schema (S26). Supports the small subset of JSON
/// Schema the templates need: <c>type</c> (object, array, string, integer, number, boolean), <c>properties</c>,
/// <c>required</c>, <c>items</c> and <c>enum</c>. Additional properties are allowed, a model often adds some.
/// </summary>
public static class TemplateSchemaValidator
{
    public const int MaxSchemaLength = 8_000;
    private static readonly HashSet<string> Types = ["object", "array", "string", "integer", "number", "boolean"];

    /// <summary>The problems of a document with respect to the schema; empty when it matches.</summary>
    public static IReadOnlyList<string> Validate(string schema, string document)
    {
        JsonNode? schemaNode, documentNode;
        try
        {
            schemaNode = JsonNode.Parse(schema);
            documentNode = JsonNode.Parse(document);
        }
        catch (JsonException ex)
        {
            return [$"Not valid JSON: {ex.Message}"];
        }

        var errors = new List<string>();
        Check(schemaNode, documentNode, "$", errors);
        return errors;
    }

    public static IReadOnlyList<string> Validate(string schema, JsonNode? document)
    {
        JsonNode? schemaNode;
        try
        {
            schemaNode = JsonNode.Parse(schema);
        }
        catch (JsonException ex)
        {
            return [$"The schema is not valid JSON: {ex.Message}"];
        }

        var errors = new List<string>();
        Check(schemaNode, document, "$", errors);
        return errors;
    }

    /// <summary>The problems of a schema definition itself (T2: custom templates); empty when it is usable.</summary>
    public static IReadOnlyList<string> ValidateDefinition(string schema)
    {
        if (string.IsNullOrWhiteSpace(schema))
            return ["The output schema is required."];
        if (schema.Length > MaxSchemaLength)
            return [$"The output schema must be at most {MaxSchemaLength} characters long."];

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(schema);
        }
        catch (JsonException)
        {
            return ["The output schema is not valid JSON."];
        }

        if (node is not JsonObject root)
            return ["The output schema must be a JSON object."];

        var errors = new List<string>();
        CheckDefinition(root, "$", errors, depth: 0);
        return errors;
    }

    private static void CheckDefinition(JsonObject schema, string path, List<string> errors, int depth)
    {
        if (depth > 8)
        {
            errors.Add($"{path}: the schema is nested too deeply.");
            return;
        }
        if (schema["type"] is not JsonValue typeValue || !typeValue.TryGetValue<string>(out var type) || !Types.Contains(type))
        {
            errors.Add($"{path}: 'type' must be one of {string.Join(", ", Types)}.");
            return;
        }

        if (type == "object" && schema["properties"] is JsonObject properties)
        {
            foreach (var (name, child) in properties)
            {
                if (child is JsonObject childSchema)
                    CheckDefinition(childSchema, $"{path}.{name}", errors, depth + 1);
                else
                    errors.Add($"{path}.{name}: a property schema must be an object.");
            }
        }
        if (type == "array" && schema["items"] is JsonObject items)
            CheckDefinition(items, $"{path}[]", errors, depth + 1);
    }

    private static void Check(JsonNode? schema, JsonNode? value, string path, List<string> errors)
    {
        if (schema is not JsonObject definition)
            return;
        var type = definition["type"]?.GetValue<string>();

        switch (type)
        {
            case "object":
                if (value is not JsonObject obj)
                {
                    errors.Add($"{path}: expected an object.");
                    return;
                }
                if (definition["required"] is JsonArray required)
                {
                    foreach (var name in required.Select(r => r?.GetValue<string>()).Where(n => n is not null))
                    {
                        if (!obj.ContainsKey(name!) || obj[name!] is null)
                            errors.Add($"{path}: the property '{name}' is missing.");
                    }
                }
                if (definition["properties"] is JsonObject properties)
                {
                    foreach (var (name, childSchema) in properties)
                    {
                        if (obj[name] is { } child)
                            Check(childSchema, child, $"{path}.{name}", errors);
                    }
                }
                break;
            case "array":
                if (value is not JsonArray array)
                {
                    errors.Add($"{path}: expected a list.");
                    return;
                }
                for (var i = 0; i < array.Count; i++)
                    Check(definition["items"], array[i], $"{path}[{i}]", errors);
                break;
            case "string":
                if (value is not JsonValue text || !text.TryGetValue<string>(out var s))
                {
                    errors.Add($"{path}: expected a string.");
                    return;
                }
                if (definition["enum"] is JsonArray allowed && !allowed.Any(a => a?.GetValue<string>() == s))
                    errors.Add($"{path}: '{s}' is not one of the allowed values.");
                break;
            case "integer":
                if (value is not JsonValue integer || !(integer.TryGetValue<long>(out _) || (integer.TryGetValue<double>(out var d) && d == Math.Floor(d))))
                    errors.Add($"{path}: expected an integer.");
                break;
            case "number":
                if (value is not JsonValue number || !number.TryGetValue<double>(out _))
                    errors.Add($"{path}: expected a number.");
                break;
            case "boolean":
                if (value is not JsonValue flag || !flag.TryGetValue<bool>(out _))
                    errors.Add($"{path}: expected true or false.");
                break;
        }
    }
}
