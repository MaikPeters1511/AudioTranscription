namespace AudioTranscription.Domain.Diarization;

/// <summary>
/// Assigns each Whisper transcript segment to the speaker whose diarization interval overlaps it the
/// most (S11-T3). Pure and model-free, so it is testable without running any diarization.
/// </summary>
public static class SpeakerOverlapAssigner
{
    /// <returns>
    /// The speaker index with the largest overlap in milliseconds, or null if no interval overlaps the
    /// segment at all (including a zero-length segment or reversed start/end, which never overlap).
    /// Ties (equal overlap) are broken by the lower speaker index, regardless of input order.
    /// </returns>
    public static int? Assign(long segmentStartMs, long segmentEndMs, IReadOnlyList<SpeakerInterval> speakerIntervals)
    {
        int? bestSpeaker = null;
        long bestOverlapMs = 0;

        foreach (var interval in speakerIntervals)
        {
            var overlapMs = Math.Min(segmentEndMs, interval.EndMs) - Math.Max(segmentStartMs, interval.StartMs);
            if (overlapMs <= 0)
                continue;

            if (bestSpeaker is null || overlapMs > bestOverlapMs ||
                (overlapMs == bestOverlapMs && interval.SpeakerIndex < bestSpeaker))
            {
                bestSpeaker = interval.SpeakerIndex;
                bestOverlapMs = overlapMs;
            }
        }

        return bestSpeaker;
    }
}
