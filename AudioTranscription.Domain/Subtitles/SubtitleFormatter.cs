using System.Text;
using AudioTranscription.Domain.Entities;

namespace AudioTranscription.Domain.Subtitles;

/// <summary>
/// Renders transcript segments as SubRip (.srt) or WebVTT (.vtt) subtitles. No I/O.
/// Empty segments are skipped, whitespace is collapsed and lines are wrapped at <see cref="MaxLineLength"/>.
/// </summary>
public static class SubtitleFormatter
{
    public const int MaxLineLength = 42;

    /// <param name="speakerNames">
    /// Maps <see cref="TranscriptSegment.SpeakerIndex"/> to a display name (S11); a segment without a
    /// speaker, or whose speaker is not in this map, gets no prefix. Omit for plain, unprefixed cues.
    /// </param>
    public static string ToSrt(IEnumerable<TranscriptSegment> segments, IReadOnlyDictionary<int, string>? speakerNames = null) =>
        string.Join("\n", Cues(segments, speakerNames).Select((cue, i) =>
            $"{i + 1}\n{Timestamp(cue.StartMs, ',')} --> {Timestamp(cue.EndMs, ',')}\n{Wrap(WithSpeakerPrefix(cue.Text, cue.Speaker))}\n"));

    /// <param name="speakerNames">See <see cref="ToSrt"/>; wraps the cue text in a WebVTT voice span (<c>&lt;v Name&gt;</c>) instead of a plain-text prefix.</param>
    public static string ToVtt(IEnumerable<TranscriptSegment> segments, IReadOnlyDictionary<int, string>? speakerNames = null) =>
        "WEBVTT\n" + string.Concat(Cues(segments, speakerNames).Select(cue =>
            $"\n{Timestamp(cue.StartMs, '.')} --> {Timestamp(cue.EndMs, '.')}\n{WrapVtt(cue)}\n"));

    private static IEnumerable<(long StartMs, long EndMs, string Text, string? Speaker)> Cues(
        IEnumerable<TranscriptSegment> segments, IReadOnlyDictionary<int, string>? speakerNames) =>
        segments
            .OrderBy(s => s.Index)
            .Select(s => (
                Start: Math.Max(0, s.StartMs), s.EndMs,
                Text: NormalizeWhitespace(s.Text),
                Speaker: s.SpeakerIndex is { } index && speakerNames is not null && speakerNames.TryGetValue(index, out var name)
                    ? name
                    : null))
            .Where(c => c.Text.Length > 0)
            .Select(c => (c.Start, Math.Max(c.Start, c.EndMs), c.Text, c.Speaker));

    /// <summary>HH:MM:SS plus milliseconds; hours keep counting beyond 99.</summary>
    private static string Timestamp(long milliseconds, char millisecondSeparator)
    {
        var hours = milliseconds / 3_600_000;
        var minutes = milliseconds / 60_000 % 60;
        var seconds = milliseconds / 1_000 % 60;
        var ms = milliseconds % 1_000;
        return $"{hours:00}:{minutes:00}:{seconds:00}{millisecondSeparator}{ms:000}";
    }

    private static string NormalizeWhitespace(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Greedy word wrap; a word longer than a line stays whole on its own line.</summary>
    private static string Wrap(string text)
    {
        var lines = new List<string>();
        var line = new StringBuilder();
        foreach (var word in text.Split(' '))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > MaxLineLength)
            {
                lines.Add(line.ToString());
                line.Clear();
            }
            if (line.Length > 0)
                line.Append(' ');
            line.Append(word);
        }
        lines.Add(line.ToString());
        return string.Join('\n', lines);
    }

    /// <summary>WebVTT cue text treats &amp; and &lt; as markup, and "--&gt;" would end the cue timing line.</summary>
    private static string EscapeVtt(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string WithSpeakerPrefix(string text, string? speaker) =>
        speaker is null ? text : $"{speaker}: {text}";

    /// <summary>Wraps the (escaped, line-wrapped) cue text in a WebVTT voice span for its speaker, if any.</summary>
    private static string WrapVtt((long StartMs, long EndMs, string Text, string? Speaker) cue)
    {
        var text = Wrap(EscapeVtt(cue.Text));
        return cue.Speaker is null ? text : $"<v {EscapeVtt(cue.Speaker)}>{text}</v>";
    }
}
