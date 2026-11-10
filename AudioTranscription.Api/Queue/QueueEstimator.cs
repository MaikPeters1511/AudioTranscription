namespace AudioTranscription.Api.Queue;

/// <summary>A finished job: file size and how long the worker needed for it.</summary>
public record FinishedRun(long Bytes, TimeSpan Processing);

/// <summary>The job the worker is processing right now.</summary>
public record RunningJob(long Bytes, DateTime StartedAtUtc);

public record RunningEstimate(double? RemainingSeconds);

public record WaitingEstimate(double? WaitSeconds, double? ProcessingSeconds);

public record QueueEstimate(RunningEstimate? Running, IReadOnlyList<WaitingEstimate> Waiting);

/// <summary>
/// Estimates waiting times (S34). The audio length of a job is only known after transcription, so the
/// file size is the proxy: the average processing speed (seconds per byte) of finished jobs is applied
/// to the sizes of the running and waiting jobs. Without finished jobs there is no estimate.
/// </summary>
public static class QueueEstimator
{
    public static double? SecondsPerByte(IEnumerable<FinishedRun> runs)
    {
        var usable = runs.Where(r => r.Bytes > 0 && r.Processing > TimeSpan.Zero).ToList();
        if (usable.Count == 0)
            return null;
        return usable.Sum(r => r.Processing.TotalSeconds) / usable.Sum(r => (double)r.Bytes);
    }

    /// <param name="speed">Seconds per byte, or null if unknown.</param>
    /// <param name="running">The job being processed, if any.</param>
    /// <param name="waiting">File sizes of the waiting jobs in processing order.</param>
    public static QueueEstimate Estimate(
        double? speed, RunningJob? running, IReadOnlyList<long> waiting, DateTime now)
    {
        RunningEstimate? runningEstimate = null;
        double? ahead = 0;
        if (running is not null)
        {
            double? remaining = speed is { } s
                ? Math.Max(0, running.Bytes * s - (now - running.StartedAtUtc).TotalSeconds)
                : null;
            runningEstimate = new RunningEstimate(remaining);
            ahead = remaining;
        }

        var result = new List<WaitingEstimate>(waiting.Count);
        foreach (var bytes in waiting)
        {
            double? processing = speed is { } s ? bytes * s : null;
            result.Add(new WaitingEstimate(ahead, processing));
            ahead = ahead is { } a && processing is { } p ? a + p : null;
        }

        return new QueueEstimate(runningEstimate, result);
    }
}
