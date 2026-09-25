namespace AudioTranscription.Domain.Search;

/// <summary>A window of text around a search match, with the match's position inside that window (S13).</summary>
public record SearchSnippet(string Text, int HighlightStart, int HighlightLength);

/// <summary>
/// Builds a snippet window around the first literal occurrence of a query word in a text, with offsets
/// for the caller to highlight (not HTML, see ADR 0005). SQL Server's FREETEXT can match a stemmed form
/// that never appears literally (e.g. "spielen" matching "spielten"); when no query word is found
/// literally, the snippet falls back to the start of the text with no highlight.
/// </summary>
public static class SearchSnippetBuilder
{
    private const int ContextChars = 80;

    public static SearchSnippet Build(string text, string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var word in words)
        {
            var index = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                continue;

            var windowStart = Math.Max(0, index - ContextChars);
            var windowEnd = Math.Min(text.Length, index + word.Length + ContextChars);
            var leadingEllipsis = windowStart > 0;
            var trailingEllipsis = windowEnd < text.Length;

            var snippet = (leadingEllipsis ? "…" : "") + text[windowStart..windowEnd] + (trailingEllipsis ? "…" : "");
            var highlightStart = index - windowStart + (leadingEllipsis ? 1 : 0);
            return new SearchSnippet(snippet, highlightStart, word.Length);
        }

        // Stemmed-only match: no literal substring to point at, so no highlight
        var head = text.Length <= ContextChars * 2 ? text : text[..(ContextChars * 2)] + "…";
        return new SearchSnippet(head, 0, 0);
    }
}
