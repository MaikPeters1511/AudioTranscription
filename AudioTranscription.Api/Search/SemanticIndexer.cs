using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Domain.Search;
using AudioTranscription.Infrastructure.Data;
using AudioTranscription.Infrastructure.Embeddings;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Api.Search;

/// <summary>Whether the vector index fits the configured embedding model (ADR 0010).</summary>
public enum IndexStatus
{
    /// <summary>Nothing was indexed yet.</summary>
    NotBuilt,
    Ready,
    /// <summary>Built with another model or dimension: it has to be rebuilt before it can be searched or extended.</summary>
    Stale,
}

public class SemanticIndexStaleException() : InvalidOperationException(
    "The semantic index was built with another embedding model. Rebuild it (POST /api/admin/search/reindex).");

/// <summary>Builds and maintains the vector index of the transcripts (S23): chunks, embeddings, Qdrant, and the record of the model used.</summary>
public class SemanticIndexer(AppDbContext db, IEmbeddingService embeddings, IVectorIndex index, ILogger<SemanticIndexer> logger)
{
    public async Task<IndexStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var state = await db.EmbeddingIndexStates.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (state is null)
            return IndexStatus.NotBuilt;
        var info = embeddings.Info;
        return state.Provider == info.Provider && state.Model == info.Model && state.Dimensions == info.Dimensions
            ? IndexStatus.Ready
            : IndexStatus.Stale;
    }

    /// <summary>
    /// Indexes the current transcript of a completed job, replacing its earlier chunks. A job that is not completed or
    /// has no text leaves no chunks. If embedding fails, the job's earlier chunks stay as they were.
    /// </summary>
    /// <exception cref="SemanticIndexStaleException">The index belongs to another embedding model.</exception>
    public async Task IndexJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(cancellationToken);
        if (status == IndexStatus.Stale)
            throw new SemanticIndexStaleException();

        var job = await db.AudioJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null || job.Status != AudioJobStatus.Completed)
            return;

        var segments = await db.TranscriptSegments.AsNoTracking()
            .Where(s => s.AudioJobId == jobId).OrderBy(s => s.Index).ToListAsync(cancellationToken);
        var chunks = SegmentChunker.Build(segments.Select(s => new TimedText(s.StartMs, s.EndMs, s.EffectiveText)).ToList());

        // Embed first: when this fails, the chunks that are already in the index stay
        var vectors = chunks.Count == 0 ? [] : await embeddings.EmbedAsync(chunks.Select(c => c.Text).ToList(), cancellationToken);

        if (status == IndexStatus.NotBuilt)
            await StartIndexAsync(cancellationToken);

        await index.ReplaceJobAsync(
            jobId, job.OwnerId ?? string.Empty,
            chunks.Select((c, i) => new IndexedChunk(c.Index, c.StartMs, c.EndMs, c.Text, vectors[i])).ToList(),
            cancellationToken);
    }

    /// <summary>
    /// Drops the index and builds it again for every completed job with the configured model (also the way to index
    /// existing data for the first time). A job that fails is logged and skipped.
    /// </summary>
    public async Task ReindexAllAsync(Action<(int Done, int Total)> progress, CancellationToken cancellationToken = default)
    {
        await index.RecreateCollectionAsync(embeddings.Info.Dimensions, cancellationToken);
        await SaveStateAsync(cancellationToken);

        var ids = await db.AudioJobs.AsNoTracking()
            .Where(j => j.Status == AudioJobStatus.Completed).OrderBy(j => j.CreatedAtUtc).Select(j => j.Id).ToListAsync(cancellationToken);
        logger.LogInformation("Rebuilding the semantic index for {Total} job(s)", ids.Count);

        var done = 0;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await IndexJobAsync(id, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not index job {JobId} while rebuilding", id);
            }
            progress((++done, ids.Count));
            if (done % 10 == 0 || done == ids.Count)
                logger.LogInformation("Rebuilding the semantic index: {Done}/{Total}", done, ids.Count);
        }
    }

    private async Task StartIndexAsync(CancellationToken cancellationToken)
    {
        await index.EnsureCollectionAsync(embeddings.Info.Dimensions, cancellationToken);
        await SaveStateAsync(cancellationToken);
    }

    private async Task SaveStateAsync(CancellationToken cancellationToken)
    {
        var info = embeddings.Info;
        var state = await db.EmbeddingIndexStates.SingleOrDefaultAsync(cancellationToken);
        if (state is null)
        {
            state = new EmbeddingIndexState();
            db.EmbeddingIndexStates.Add(state);
        }
        state.Provider = info.Provider;
        state.Model = info.Model;
        state.Dimensions = info.Dimensions;
        state.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
