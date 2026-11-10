namespace AudioTranscription.Domain.Search;

/// <summary>A search hit with the time it points to; <c>StartMs</c> is null for a hit on the job's whole transcript.</summary>
public record RankedHit(Guid JobId, long? StartMs, long? EndMs, string Text);

/// <summary>A hit after fusion: found by the full-text search, the vector search or both.</summary>
public record FusedHit(Guid JobId, RankedHit? FullText, RankedHit? Semantic, double Score);

/// <summary>
/// Combines the ranking of the full-text search (S13) and the vector search (S23) with Reciprocal Rank Fusion
/// (ADR 0010): <c>score = Σ 1 / (K + rank)</c>. A full-text hit and a semantic hit are the same hit when the full-text
/// time lies in the semantic chunk's time range of the same job.
/// </summary>
public static class ReciprocalRankFusion
{
    public const int K = 60;

    public static IReadOnlyList<FusedHit> Fuse(IReadOnlyList<RankedHit> fullText, IReadOnlyList<RankedHit> semantic)
    {
        var fused = new List<(RankedHit? FullText, RankedHit? Semantic, double Score, int Order)>();

        for (var i = 0; i < semantic.Count; i++)
            fused.Add((null, semantic[i], 1.0 / (K + i + 1), fused.Count));

        for (var i = 0; i < fullText.Count; i++)
        {
            var hit = fullText[i];
            var score = 1.0 / (K + i + 1);
            var match = hit.StartMs is { } at
                ? fused.FindIndex(f => f.FullText is null && f.Semantic is { } s && s.JobId == hit.JobId
                                       && s.StartMs <= at && at <= s.EndMs)
                : -1;
            if (match >= 0)
                fused[match] = (hit, fused[match].Semantic, fused[match].Score + score, fused[match].Order);
            else
                fused.Add((hit, null, score, fused.Count));
        }

        return fused
            .OrderByDescending(f => f.Score)
            .ThenBy(f => f.Order)
            .Select(f => new FusedHit((f.FullText ?? f.Semantic)!.JobId, f.FullText, f.Semantic, f.Score))
            .ToList();
    }
}
