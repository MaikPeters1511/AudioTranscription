namespace AudioTranscription.Domain.Chapters;

/// <summary>
/// Splits a long transcript into windows for the Map step (S25-T1): always on segment boundaries, so no segment
/// is cut in two, lost or repeated. A single segment longer than the limit gets a window of its own.
/// </summary>
public static class ChapterWindowing
{
    /// <summary>The text of a segment as it is shown to the LLM, numbered so the model can refer to it.</summary>
    public static string Line(ChapterSegment segment) => $"[{segment.Index}] {segment.Text.Trim()}";

    public static IReadOnlyList<IReadOnlyList<ChapterSegment>> Split(IReadOnlyList<ChapterSegment> segments, int maxChars)
    {
        var windows = new List<IReadOnlyList<ChapterSegment>>();
        var current = new List<ChapterSegment>();
        var length = 0;
        foreach (var segment in segments)
        {
            var lineLength = Line(segment).Length + 1;
            if (current.Count > 0 && length + lineLength > maxChars)
            {
                windows.Add(current);
                current = [];
                length = 0;
            }
            current.Add(segment);
            length += lineLength;
        }
        if (current.Count > 0)
            windows.Add(current);
        return windows;
    }
}
