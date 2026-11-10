namespace AudioTranscription.Domain.Glossary;

/// <summary>
/// The Whisper initial prompt built from glossary terms (S22). Whisper only looks at about 224 tokens of
/// prompt; a glossary beyond that is rejected when it is saved instead of being cut off silently.
/// </summary>
public static class GlossaryPrompt
{
    /// <summary>Whisper's prompt window (half of its 448-token context).</summary>
    public const int MaxTokens = 224;

    /// <summary>Conservative: names and technical terms split into more tokens than plain German or English words.</summary>
    private const double CharactersPerToken = 3.0;

    public static string Build(IEnumerable<string> terms) => string.Join(", ", terms);

    public static int EstimateTokens(string text) => (int)Math.Ceiling(text.Length / CharactersPerToken);

    public static bool Fits(IEnumerable<string> terms) => EstimateTokens(Build(terms)) <= MaxTokens;
}
