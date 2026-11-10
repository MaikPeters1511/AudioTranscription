using AudioTranscription.Domain.Entities;

namespace AudioTranscription.Domain.Export;

/// <summary>Builds <see cref="ExportDocument"/>s from segments or variant text. No I/O.</summary>
public static class ExportDocumentBuilder
{
    public static ExportDocument FromSegments(
        string title, DateTime createdAtUtc, IEnumerable<TranscriptSegment> segments,
        IReadOnlyDictionary<int, string>? speakerNames, ExportOptions options)
    {
        var blocks = new List<ExportBlock>();
        int? lastSpeaker = null;
        foreach (var segment in segments.OrderBy(s => s.Index))
        {
            var text = Normalize(segment.EffectiveText);
            if (text.Length == 0)
                continue;

            var speaker = options.Speakers && segment.SpeakerIndex is { } index && speakerNames is not null && speakerNames.TryGetValue(index, out var name)
                ? name
                : null;

            if (speaker is not null && blocks.Count > 0 && lastSpeaker == segment.SpeakerIndex && blocks[^1].Speaker == speaker)
            {
                var previous = blocks[^1];
                blocks[^1] = previous with { Text = $"{previous.Text} {text}" };
                continue;
            }

            lastSpeaker = segment.SpeakerIndex;
            blocks.Add(new ExportBlock(speaker, options.Timestamps ? Math.Max(0, segment.StartMs) : null, text));
        }

        return new ExportDocument(title, createdAtUtc, null, blocks);
    }

    /// <summary>A variant has no timing: one block per paragraph (blank-line separated).</summary>
    public static ExportDocument FromVariantText(string title, DateTime createdAtUtc, string variantLabel, string text)
    {
        var blocks = text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .Select(p => new ExportBlock(null, null, p))
            .ToList();
        return new ExportDocument(title, createdAtUtc, variantLabel, blocks);
    }

    /// <summary>HH:MM:SS; hours keep counting beyond 99.</summary>
    public static string Timestamp(long milliseconds) =>
        $"{milliseconds / 3_600_000:00}:{milliseconds / 60_000 % 60:00}:{milliseconds / 1_000 % 60:00}";

    private static string Normalize(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
