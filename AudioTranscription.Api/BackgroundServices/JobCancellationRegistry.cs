using System.Collections.Concurrent;

namespace AudioTranscription.Api.BackgroundServices;

/// <summary>
/// Tracks the cancellation source of the job the worker is currently processing,
/// so the API can cancel it on user request.
/// </summary>
public class JobCancellationRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();

    /// <summary>Creates a token for the job that also fires on application shutdown.</summary>
    public CancellationTokenSource Register(Guid jobId, CancellationToken stoppingToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _running[jobId] = cts;
        return cts;
    }

    /// <summary>Removes the job; the caller owns and disposes the token source.</summary>
    public void Unregister(Guid jobId) => _running.TryRemove(jobId, out _);

    /// <summary>Requests cancellation of a running job. Returns false if the job is not running.</summary>
    public bool Cancel(Guid jobId)
    {
        if (!_running.TryGetValue(jobId, out var cts))
            return false;

        cts.Cancel();
        return true;
    }
}
