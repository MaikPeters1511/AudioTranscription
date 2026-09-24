using System.Text;

namespace AudioTranscription.Infrastructure.Services;

/// <summary>
/// Splits long text into chunks of at most <c>maxChunkLength</c> characters for Map-Reduce
/// post-processing (S10-T4), preferring paragraph and sentence boundaries and never splitting a word.
/// No tokenizer is available for the configured Ollama model, so characters approximate tokens.
/// </summary>
public static class TranscriptChunker
{
    public static IReadOnlyList<string> Chunk(string text, int maxChunkLength)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return [];

        var chunks = new List<string>();
        var current = new StringBuilder();
        foreach (var word in words)
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > maxChunkLength)
            {
                chunks.Add(current.ToString());
                current.Clear();
            }
            if (current.Length > 0)
                current.Append(' ');
            current.Append(word);
        }
        if (current.Length > 0)
            chunks.Add(current.ToString());

        return chunks;
    }
}
