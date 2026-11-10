namespace AudioTranscription.Api.BackgroundServices;

/// <summary>
/// In-process priority queue for transcription jobs (S34): the job with the highest priority runs first,
/// jobs with equal priority run in arrival order (FIFO). The priority of a waiting job can be changed.
/// No external message broker needed for local operation.
/// </summary>
public class TranscriptionQueue
{
    private readonly object _gate = new();
    private readonly List<Entry> _entries = [];

    // One permit per waiting entry; lets the single reader wait without polling
    private readonly SemaphoreSlim _available = new(0);
    private long _sequence;

    /// <summary>Jobs waiting to be picked up by the worker (reported as a metric, S29).</summary>
    public int Count
    {
        get
        {
            lock (_gate)
                return _entries.Count;
        }
    }

    public ValueTask EnqueueAsync(TranscriptionJobRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            _entries.Add(new Entry(request, _sequence++));
        _available.Release();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Changes the priority of a waiting job. Returns false if the job is not waiting (unknown, running or finished).
    /// </summary>
    public bool SetPriority(Guid jobId, int priority)
    {
        lock (_gate)
        {
            var entry = _entries.FirstOrDefault(e => e.Request.JobId == jobId);
            if (entry is null)
                return false;
            entry.Request = entry.Request with { Priority = priority };
            return true;
        }
    }

    /// <summary>The waiting jobs in the order they will be processed.</summary>
    public IReadOnlyList<QueuedJob> Snapshot()
    {
        lock (_gate)
            return _entries
                .OrderByDescending(e => e.Request.Priority)
                .ThenBy(e => e.Sequence)
                .Select(e => new QueuedJob(e.Request.JobId, e.Request.Priority))
                .ToList();
    }

    /// <summary>
    /// Non-blocking read, used by tests to inspect the queue.
    /// </summary>
    internal bool TryRead([System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out TranscriptionJobRequest request)
    {
        if (!_available.Wait(0))
        {
            request = null;
            return false;
        }

        request = TakeNext();
        return true;
    }

    public async IAsyncEnumerable<TranscriptionJobRequest> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (true)
        {
            await _available.WaitAsync(cancellationToken);
            yield return TakeNext();
        }
    }

    private TranscriptionJobRequest TakeNext()
    {
        lock (_gate)
        {
            var next = _entries
                .OrderByDescending(e => e.Request.Priority)
                .ThenBy(e => e.Sequence)
                .First();
            _entries.Remove(next);
            return next.Request;
        }
    }

    private sealed class Entry(TranscriptionJobRequest request, long sequence)
    {
        public TranscriptionJobRequest Request { get; set; } = request;
        public long Sequence { get; } = sequence;
    }
}

public record TranscriptionJobRequest(Guid JobId, string FilePath, int Priority = 0);

/// <summary>A waiting job as listed by <see cref="TranscriptionQueue.Snapshot"/>.</summary>
public record QueuedJob(Guid JobId, int Priority);
