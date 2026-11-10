using AudioTranscription.Domain.Entities;

namespace AudioTranscription.Domain.Chapters;

/// <summary>Reduce step (S25-T1): turns the proposals of all windows into one clean list of chapters.</summary>
public static class ChapterMerger
{
    public static IReadOnlyList<ChapterDraft> Merge(IEnumerable<ChapterDraft> drafts, int firstIndex, int lastIndex)
    {
        var sorted = drafts
            .Where(d => d.StartSegmentIndex >= firstIndex && d.StartSegmentIndex <= lastIndex)
            .OrderBy(d => d.StartSegmentIndex)
            .GroupBy(d => d.StartSegmentIndex)
            .Select(g => g.First()) // two chapters cannot start at the same segment
            .ToList();

        if (sorted.Count == 0)
            return [new ChapterDraft(firstIndex, string.Empty, string.Empty)];

        // The recording is covered from its first segment on: the first chapter starts there
        sorted[0] = sorted[0] with { StartSegmentIndex = firstIndex };

        // A topic that runs across a window border comes back as two chapters with the same title
        var merged = new List<ChapterDraft>();
        foreach (var draft in sorted)
        {
            if (merged.Count > 0 && SameTitle(merged[^1].Title, draft.Title) && draft.Title.Length > 0)
                continue;
            merged.Add(draft);
        }
        return merged;
    }

    private static bool SameTitle(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>Turns the merged drafts into chapters whose limits lie on segment boundaries, with no gap and no overlap.</summary>
public static class ChapterBuilder
{
    public static IReadOnlyList<TranscriptChapter> Build(Guid jobId, IReadOnlyList<ChapterSegment> segments, IEnumerable<ChapterDraft> drafts)
    {
        var byIndex = segments.ToDictionary(s => s.Index);
        var starts = drafts
            .Where(d => byIndex.ContainsKey(d.StartSegmentIndex))
            .OrderBy(d => d.StartSegmentIndex)
            .ToList();

        var lastEnd = segments.Count == 0 ? 0 : segments.Max(s => s.EndMs);
        var chapters = new List<TranscriptChapter>();
        for (var i = 0; i < starts.Count; i++)
        {
            var startMs = byIndex[starts[i].StartSegmentIndex].StartMs;
            var endMs = i + 1 < starts.Count ? byIndex[starts[i + 1].StartSegmentIndex].StartMs : lastEnd;
            chapters.Add(new TranscriptChapter
            {
                AudioJobId = jobId,
                Position = i,
                StartMs = startMs,
                EndMs = Math.Max(startMs, endMs),
                Title = starts[i].Title,
                Summary = starts[i].Summary,
            });
        }
        return chapters;
    }
}
