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

    public static string ToSrt(IEnumerable<TranscriptSegment> segments) =>
        string.Join("\n", Cues(segments).Select((cue, i) =>
            $"{i + 1}\n{Timestamp(cue.StartMs, ',')} --> {Timestamp(cue.EndMs, ',')}\n{Wrap(cue.Text)}\n"));

    public static string ToVtt(IEnumerable<TranscriptSegment> segments) =>
        "WEBVTT\n" + string.Concat(Cues(segments).Select(cue =>
            $"\n{Timestamp(cue.StartMs, '.')} --> {Timestamp(cue.EndMs, '.')}\n{Wrap(EscapeVtt(cue.Text))}\n"));

    private static IEnumerable<(long StartMs, long EndMs, string Text)> Cues(IEnumerable<TranscriptSegment> segments) =>
        segments
            .OrderBy(s => s.Index)
            .Select(s => (Start: Math.Max(0, s.StartMs), s.EndMs, Text: NormalizeWhitespace(s.Text)))
            .Where(c => c.Text.Length > 0)
            .Select(c => (c.Start, Math.Max(c.Start, c.EndMs), c.Text));

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
}
