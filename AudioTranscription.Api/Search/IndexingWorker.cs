using System.Threading.Channels;
using AudioTranscription.Infrastructure.Embeddings;

namespace AudioTranscription.Api.Search;

/// <summary>A request to the indexing worker: index one job, or rebuild everything when <see cref="JobId"/> is null.</summary>
public record IndexingRequest(Guid? JobId);

/// <summary>In-process queue of jobs to index (S23); a job that is already waiting is not queued a second time.</summary>
public class IndexingQueue
{
    private readonly Channel<IndexingRequest> _channel = Channel.CreateUnbounded<IndexingRequest>(new UnboundedChannelOptions { SingleReader = true });
    private readonly HashSet<Guid> _waiting = [];
    private readonly object _lock = new();

    public void Enqueue(Guid jobId)
    {
        lock (_lock)
        {
            if (!_waiting.Add(jobId))
                return;
        }
        _channel.Writer.TryWrite(new IndexingRequest(jobId));
    }

    public void EnqueueReindex() => _channel.Writer.TryWrite(new IndexingRequest(null));

    internal void Taken(Guid jobId)
    {
        lock (_lock)
            _waiting.Remove(jobId);
    }

    public IAsyncEnumerable<IndexingRequest> ReadAllAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>Progress of the running rebuild, for the admin status.</summary>
public class ReindexProgress
{
    private int _done, _total;
    private volatile bool _running;

    public bool Running => _running;
    public int Done => Volatile.Read(ref _done);
    public int Total => Volatile.Read(ref _total);

    internal void Start() { Volatile.Write(ref _done, 0); Volatile.Write(ref _total, 0); _running = true; }
    internal void Report(int done, int total) { Volatile.Write(ref _done, done); Volatile.Write(ref _total, total); }
    internal void Finish() => _running = false;

    /// <summary>Marks the rebuild as running before the worker picks it up, so a second request is refused.</summary>
    public bool TryBegin()
    {
        lock (this)
        {
            if (_running)
                return false;
            Start();
            return true;
        }
    }
}

/// <summary>
/// Indexes jobs in the background (S23). A failure is logged and never touches the job itself: a transcript stays usable
/// when the embedding service or Qdrant is down, and the rebuild fixes whatever was missed.
/// </summary>
public class IndexingWorker(
    IndexingQueue queue,
    ReindexProgress progress,
    IServiceScopeFactory scopeFactory,
    ILogger<IndexingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(request, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error while indexing");
            }
        }
    }

    internal async Task ProcessAsync(IndexingRequest request, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var indexer = scope.ServiceProvider.GetRequiredService<SemanticIndexer>();

        if (request.JobId is not { } jobId)
        {
            progress.Start();
            try
            {
                await indexer.ReindexAllAsync(p => progress.Report(p.Done, p.Total), cancellationToken);
            }
            finally
            {
                progress.Finish();
            }
            return;
        }

        queue.Taken(jobId);
        try
        {
            await indexer.IndexJobAsync(jobId, cancellationToken);
        }
        catch (SemanticIndexStaleException)
        {
            logger.LogWarning("Job {JobId} not indexed: the embedding model changed, the index has to be rebuilt first", jobId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The job stays as it is; a rebuild picks it up later
            logger.LogWarning(ex, "Indexing job {JobId} failed", jobId);
        }
    }
}

/// <summary>The places that change a transcript call this; it does nothing when semantic search is not configured.</summary>
public static class SemanticIndexHooks
{
    public static void JobChanged(IServiceProvider services, Guid jobId) => services.GetService<IndexingQueue>()?.Enqueue(jobId);

    /// <summary>Removes the vectors of a deleted job. Best effort: leftovers cannot be found (permission filter) and vanish with the next rebuild.</summary>
    public static async Task JobDeletedAsync(IServiceProvider services, Guid jobId, ILogger logger, CancellationToken cancellationToken = default)
    {
        var index = services.GetService<IVectorIndex>();
        if (index is null)
            return;
        try
        {
            await index.DeleteJobAsync(jobId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not remove the vectors of job {JobId}", jobId);
        }
    }
}
