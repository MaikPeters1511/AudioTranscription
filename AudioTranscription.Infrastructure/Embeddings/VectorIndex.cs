using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace AudioTranscription.Infrastructure.Embeddings;

/// <summary>A chunk of a transcript with its vector, ready for the index.</summary>
public record IndexedChunk(int Index, long StartMs, long EndMs, string Text, float[] Vector);

/// <summary>A search result of the vector index.</summary>
public record VectorHit(Guid JobId, long StartMs, long EndMs, string Text, float Score);

/// <summary>The vector index of transcript chunks (S23, ADR 0010); Qdrant is the implementation.</summary>
public interface IVectorIndex
{
    /// <summary>Creates the collection if it does not exist yet.</summary>
    Task EnsureCollectionAsync(int dimensions, CancellationToken cancellationToken = default);

    /// <summary>Drops the collection and creates it empty, e.g. after a change of the embedding model.</summary>
    Task RecreateCollectionAsync(int dimensions, CancellationToken cancellationToken = default);

    /// <summary>Makes <paramref name="chunks"/> the chunks of a job: earlier ones of the job are removed first.</summary>
    Task ReplaceJobAsync(Guid jobId, string ownerId, IReadOnlyList<IndexedChunk> chunks, CancellationToken cancellationToken = default);

    Task DeleteJobAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>The nearest chunks among those of <paramref name="allowedJobIds"/> only; nothing outside them is ever returned.</summary>
    Task<IReadOnlyList<VectorHit>> SearchAsync(
        float[] vector, IReadOnlyCollection<Guid> allowedJobIds, int limit, int offset = 0, CancellationToken cancellationToken = default);

    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IVectorIndex"/> on Qdrant: collection <c>transcript_chunks</c> with cosine distance. The payload holds
/// <c>jobId</c>, <c>ownerId</c>, the time range, <c>variant</c> and the chunk text; none of it is ever logged.
/// </summary>
public class QdrantVectorIndex(QdrantClient client, ILogger<QdrantVectorIndex> logger) : IVectorIndex
{
    public const string CollectionName = "transcript_chunks";

    public async Task EnsureCollectionAsync(int dimensions, CancellationToken cancellationToken = default)
    {
        if (await client.CollectionExistsAsync(CollectionName, cancellationToken))
            return;
        await CreateAsync(dimensions, cancellationToken);
    }

    public async Task RecreateCollectionAsync(int dimensions, CancellationToken cancellationToken = default)
    {
        if (await client.CollectionExistsAsync(CollectionName, cancellationToken))
            await client.DeleteCollectionAsync(CollectionName, cancellationToken: cancellationToken);
        await CreateAsync(dimensions, cancellationToken);
    }

    private async Task CreateAsync(int dimensions, CancellationToken cancellationToken)
    {
        await client.CreateCollectionAsync(
            CollectionName,
            new VectorParams { Size = (ulong)dimensions, Distance = Distance.Cosine },
            cancellationToken: cancellationToken);
        // The job filter of every search and the delete by job both use this field
        await client.CreatePayloadIndexAsync(CollectionName, "jobId", PayloadSchemaType.Keyword, cancellationToken: cancellationToken);
        logger.LogInformation("Created Qdrant collection {Collection} with {Dimensions} dimensions", CollectionName, dimensions);
    }

    public async Task ReplaceJobAsync(Guid jobId, string ownerId, IReadOnlyList<IndexedChunk> chunks, CancellationToken cancellationToken = default)
    {
        await DeleteJobAsync(jobId, cancellationToken);
        if (chunks.Count == 0)
            return;

        var points = chunks.Select(chunk =>
        {
            var point = new PointStruct
            {
                Id = Domain.Search.ChunkId.For(jobId, chunk.Index),
                Vectors = chunk.Vector,
            };
            point.Payload["jobId"] = jobId.ToString("D");
            point.Payload["ownerId"] = ownerId;
            point.Payload["segmentStartMs"] = chunk.StartMs;
            point.Payload["segmentEndMs"] = chunk.EndMs;
            point.Payload["variant"] = "raw";
            point.Payload["text"] = chunk.Text;
            return point;
        }).ToList();

        await client.UpsertAsync(CollectionName, points, wait: true, cancellationToken: cancellationToken);
        logger.LogInformation("Indexed {Count} chunk(s) of job {JobId}", chunks.Count, jobId);
    }

    public async Task DeleteJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        if (!await client.CollectionExistsAsync(CollectionName, cancellationToken))
            return;
        await client.DeleteAsync(CollectionName, Qdrant.Client.Grpc.Conditions.MatchKeyword("jobId", jobId.ToString("D")), wait: true, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<VectorHit>> SearchAsync(
        float[] vector, IReadOnlyCollection<Guid> allowedJobIds, int limit, int offset = 0, CancellationToken cancellationToken = default)
    {
        if (allowedJobIds.Count == 0)
            return [];

        var filter = new Filter();
        filter.Must.Add(Qdrant.Client.Grpc.Conditions.Match("jobId", allowedJobIds.Select(id => id.ToString("D")).ToList()));

        var results = await client.SearchAsync(
            CollectionName, vector, filter: filter, limit: (ulong)limit, offset: (ulong)offset,
            payloadSelector: true, cancellationToken: cancellationToken);

        return results.Select(r => new VectorHit(
            Guid.Parse(r.Payload["jobId"].StringValue),
            r.Payload["segmentStartMs"].IntegerValue,
            r.Payload["segmentEndMs"].IntegerValue,
            r.Payload["text"].StringValue,
            r.Score)).ToList();
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await client.HealthAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Qdrant is not reachable");
            return false;
        }
    }
}
