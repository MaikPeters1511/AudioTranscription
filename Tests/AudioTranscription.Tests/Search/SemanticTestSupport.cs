using AudioTranscription.Infrastructure.Embeddings;

namespace AudioTranscription.Tests.Search;

/// <summary>
/// A tiny bag-of-words "embedding" with a synonym table, so tests can check the whole pipeline (chunks, index, permission
/// filter, ranking) without a model: words that mean the same land on the same dimension.
/// </summary>
public class FakeEmbeddingService : IEmbeddingService
{
    public static readonly string[] Concepts = ["geld", "wetter", "urlaub", "technik", "essen"];

    private static readonly Dictionary<string, string> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["budget"] = "geld", ["budgetrunde"] = "geld", ["kosten"] = "geld", ["euro"] = "geld", ["geld"] = "geld", ["finanzen"] = "geld",
        ["regen"] = "wetter", ["sonne"] = "wetter", ["wetter"] = "wetter", ["temperatur"] = "wetter",
        ["ferien"] = "urlaub", ["urlaub"] = "urlaub", ["reise"] = "urlaub",
        ["server"] = "technik", ["software"] = "technik", ["technik"] = "technik", ["rechner"] = "technik",
        ["mittagessen"] = "essen", ["essen"] = "essen", ["pizza"] = "essen",
    };

    public EmbeddingModelInfo Info { get; set; } = new("Fake", "fake-bow", Concepts.Length);
    public int Calls { get; private set; }
    public bool Fail { get; set; }

    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        Calls++;
        if (Fail)
            throw new HttpRequestException("Embedding-Dienst nicht erreichbar");
        return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(Embed).ToList());
    }

    private static float[] Embed(string text)
    {
        var vector = new float[Concepts.Length];
        foreach (var word in text.Split([' ', '.', ',', '?', '!', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (Synonyms.TryGetValue(word, out var concept))
                vector[Array.IndexOf(Concepts, concept)] += 1;
        }
        // A text without any known word still needs a direction
        if (vector.All(v => v == 0))
            vector[^1] = 0.001f;
        var length = MathF.Sqrt(vector.Sum(v => v * v));
        return vector.Select(v => v / length).ToArray();
    }
}

/// <summary>An in-memory <see cref="IVectorIndex"/> with cosine ranking, so the application logic can be tested without Qdrant.</summary>
public class InMemoryVectorIndex : IVectorIndex
{
    public record Entry(Guid JobId, string OwnerId, IndexedChunk Chunk);

    public List<Entry> Entries { get; } = [];
    public int? Dimensions { get; private set; }
    public int Recreated { get; private set; }

    public Task EnsureCollectionAsync(int dimensions, CancellationToken cancellationToken = default)
    {
        Dimensions ??= dimensions;
        return Task.CompletedTask;
    }

    public Task RecreateCollectionAsync(int dimensions, CancellationToken cancellationToken = default)
    {
        Entries.Clear();
        Dimensions = dimensions;
        Recreated++;
        return Task.CompletedTask;
    }

    public Task ReplaceJobAsync(Guid jobId, string ownerId, IReadOnlyList<IndexedChunk> chunks, CancellationToken cancellationToken = default)
    {
        Entries.RemoveAll(e => e.JobId == jobId);
        Entries.AddRange(chunks.Select(c => new Entry(jobId, ownerId, c)));
        return Task.CompletedTask;
    }

    public Task DeleteJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        Entries.RemoveAll(e => e.JobId == jobId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VectorHit>> SearchAsync(
        float[] vector, IReadOnlyCollection<Guid> allowedJobIds, int limit, int offset = 0, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<VectorHit> hits = Entries
            .Where(e => allowedJobIds.Contains(e.JobId))
            .Select(e => new VectorHit(e.JobId, e.Chunk.StartMs, e.Chunk.EndMs, e.Chunk.Text, e.Chunk.Vector.Zip(vector, (a, b) => a * b).Sum()))
            .OrderByDescending(h => h.Score)
            .Skip(offset).Take(limit)
            .ToList();
        return Task.FromResult(hits);
    }

    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
}
