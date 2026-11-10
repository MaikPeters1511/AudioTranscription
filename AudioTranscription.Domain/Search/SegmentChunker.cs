using System.Security.Cryptography;
using System.Text;

namespace AudioTranscription.Domain.Search;

/// <summary>A piece of the transcript with its time range.</summary>
public record TimedText(long StartMs, long EndMs, string Text);

/// <summary>An overlapping section of a transcript that is embedded as one unit (S23).</summary>
public record TranscriptChunk(int Index, long StartMs, long EndMs, string Text);

/// <summary>
/// Builds the chunks for the vector index (S23, ADR 0010): consecutive segments up to about <c>targetMs</c> long and
/// <c>maxChars</c> characters, each chunk starting <c>overlapMs</c> before the end of the previous one. Chunk limits lie on
/// segment boundaries; a single segment longer than <c>maxChars</c> is split between words and keeps its time range.
/// </summary>
public static class SegmentChunker
{
    public static IReadOnlyList<TranscriptChunk> Build(
        IReadOnlyList<TimedText> segments, long targetMs = 45_000, long overlapMs = 15_000, int maxChars = 1_200)
    {
        var parts = segments.Where(s => !string.IsNullOrWhiteSpace(s.Text)).ToList();
        var chunks = new List<TranscriptChunk>();
        var start = 0;
        while (start < parts.Count)
        {
            var end = start;           // index of the last segment in the chunk
            var length = parts[start].Text.Trim().Length;
            while (end + 1 < parts.Count
                   && parts[end].EndMs - parts[start].StartMs < targetMs
                   && length + 1 + parts[end + 1].Text.Trim().Length <= maxChars)
            {
                end++;
                length += 1 + parts[end].Text.Trim().Length;
            }

            var text = string.Join(" ", parts.Skip(start).Take(end - start + 1).Select(p => p.Text.Trim()));
            var startMs = parts[start].StartMs;
            var endMs = parts[end].EndMs;
            foreach (var piece in Split(text, maxChars))
                chunks.Add(new TranscriptChunk(chunks.Count, startMs, endMs, piece));

            if (end + 1 >= parts.Count)
                break;

            // The next chunk begins in the overlap, but always moves forward by at least one segment
            var next = end + 1;
            while (next - 1 > start && parts[next - 1].StartMs >= endMs - overlapMs)
                next--;
            start = Math.Max(next, start + 1);
        }
        return chunks;
    }

    private static IEnumerable<string> Split(string text, int maxChars)
    {
        if (text.Length <= maxChars)
        {
            yield return text;
            yield break;
        }

        var current = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > maxChars)
            {
                yield return current.ToString();
                current.Clear();
            }
            if (current.Length > 0)
                current.Append(' ');
            current.Append(word);
        }
        if (current.Length > 0)
            yield return current.ToString();
    }
}

/// <summary>Stable ids for chunks, so indexing a job again overwrites its chunks instead of duplicating them.</summary>
public static class ChunkId
{
    public static Guid For(Guid jobId, int chunkIndex)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes($"{jobId:N}:{chunkIndex}"));
        return new Guid(hash);
    }
}
