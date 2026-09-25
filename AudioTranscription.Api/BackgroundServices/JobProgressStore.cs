using System.Collections.Concurrent;

namespace AudioTranscription.Api.BackgroundServices;

/// <summary>
/// Latest progress of running jobs, kept in memory only (no database write per percent), so clients
/// that load a job while it runs get the current value instead of waiting for the next event.
/// </summary>
public class JobProgressStore
{
    private readonly ConcurrentDictionary<Guid, int> _progress = new();

    public void Set(Guid jobId, int percent) => _progress[jobId] = percent;

    public int? Get(Guid jobId) => _progress.TryGetValue(jobId, out var percent) ? percent : null;

    public void Remove(Guid jobId) => _progress.TryRemove(jobId, out _);
}
