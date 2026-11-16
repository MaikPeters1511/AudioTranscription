namespace AudioTranscription.Api.Configuration;

/// <summary>
/// Configuration section "Embedding" (S23, ADR 0010). Local LM Studio is the default; OpenAI (or any OpenAI-compatible server
/// such as LM Studio) is used only when <see cref="Provider"/> says so, and the API key comes from the environment or the
/// secret manager, never from a file in the repository.
/// </summary>
public class EmbeddingOptions
{
    public const string SectionName = "Embedding";
    public const string LmStudioProvider = "LmStudio";
    public const string OpenAiProvider = "OpenAI";

    public string Provider { get; set; } = LmStudioProvider;
    /// <summary>Model name of the provider; the default suits the LM Studio provider.</summary>
    public string Model { get; set; } = LmStudioConnection.DefaultEmbeddingModel;
    /// <summary>Length of the vectors; has to match what the model produces (OpenAI models can be asked for it).</summary>
    public int Dimensions { get; set; } = 1024;
    /// <summary>Server address; falls back to <c>LmStudio:Endpoint</c> when empty.</summary>
    public string? Endpoint { get; set; }
    /// <summary>OpenAI API key, only needed (and only used) with the OpenAI provider.</summary>
    public string? ApiKey { get; set; }
}
