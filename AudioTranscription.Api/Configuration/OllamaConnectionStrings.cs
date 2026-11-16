namespace AudioTranscription.Api.Configuration;

/// <summary>
/// Resolves the endpoint of a local Ollama model. Older Aspire versions name the connection string after the model
/// ("llama3.2") and provide a plain URL; Aspire 13 prefixes the resource ("ollama-llama3-2" / "ollama_llama3_2") and
/// provides <c>Endpoint=http://host:port;Model=...</c>. All variants are accepted and reduced to the plain endpoint URL.
/// </summary>
public static class OllamaConnectionStrings
{
    public static string? Get(IConfiguration configuration, string model)
    {
        var dashed = model.Replace('.', '-');
        var underscored = model.Replace('.', '_').Replace('-', '_');
        string[] names = [model, $"ollama-{dashed}", $"ollama_{underscored}"];
        var value = names
            .Select(configuration.GetConnectionString)
            .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));
        return value is null ? null : ExtractEndpoint(value);
    }

    internal static string ExtractEndpoint(string connectionString)
    {
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            const string prefix = "Endpoint=";
            if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return part[prefix.Length..];
        }
        return connectionString;
    }
}
