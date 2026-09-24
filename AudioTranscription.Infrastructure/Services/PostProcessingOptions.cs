namespace AudioTranscription.Infrastructure.Services;

/// <summary>Configuration section "PostProcessing" (S10).</summary>
public class PostProcessingOptions
{
    public const string SectionName = "PostProcessing";

    /// <summary>
    /// Approximate character limit per chunk for Map-Reduce chunking (Summary/ActionItems).
    /// No tokenizer is available for the configured Ollama model, so characters approximate tokens.
    /// </summary>
    public int MaxChunkLength { get; set; } = 6000;
}
