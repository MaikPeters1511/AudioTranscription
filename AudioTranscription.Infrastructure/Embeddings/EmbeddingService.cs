using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace AudioTranscription.Infrastructure.Embeddings;

/// <summary>Which model produced the vectors; an index is only usable with the model it was built with (ADR 0010).</summary>
public record EmbeddingModelInfo(string Provider, string Model, int Dimensions);

/// <summary>Turns texts into vectors (EN-4); local (Ollama) by default, OpenAI(-compatible) only when configured.</summary>
public interface IEmbeddingService
{
    EmbeddingModelInfo Info { get; }

    /// <summary>One normalized vector per text, in the same order.</summary>
    /// <exception cref="Exception">The model could not be reached or answered with unusable vectors.</exception>
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IEmbeddingService"/> on top of any <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/> (Microsoft.Extensions.AI):
/// sends the texts in batches, retries transient failures (CLAUDE.md: resilience for AI calls), checks the dimension
/// and normalizes the vectors so that cosine distance is a plain dot product.
/// </summary>
public class GeneratorEmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;
    private readonly ILogger<GeneratorEmbeddingService> _logger;
    private readonly int _batchSize;

    private static readonly ResiliencePipeline RetryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 2,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(50),
            ShouldHandle = new PredicateBuilder().Handle<Exception>(ex => ex is not OperationCanceledException and not InvalidOperationException),
        })
        .Build();

    public GeneratorEmbeddingService(
        IEmbeddingGenerator<string, Embedding<float>> generator, EmbeddingModelInfo info, ILogger<GeneratorEmbeddingService> logger, int batchSize = 16)
    {
        _generator = generator;
        _logger = logger;
        _batchSize = Math.Max(1, batchSize);
        Info = info;
    }

    public EmbeddingModelInfo Info { get; }

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        var vectors = new List<float[]>(texts.Count);
        for (var offset = 0; offset < texts.Count; offset += _batchSize)
        {
            var batch = texts.Skip(offset).Take(_batchSize).ToList();
            var embeddings = await RetryPipeline.ExecuteAsync(
                async token => await _generator.GenerateAsync(batch, cancellationToken: token), cancellationToken);

            if (embeddings.Count != batch.Count)
                throw new InvalidOperationException($"The embedding model returned {embeddings.Count} vectors for {batch.Count} texts.");

            foreach (var embedding in embeddings)
            {
                var vector = embedding.Vector.ToArray();
                if (vector.Length != Info.Dimensions)
                {
                    _logger.LogError("Embedding model {Model} returned dimension {Actual}, expected {Expected}", Info.Model, vector.Length, Info.Dimensions);
                    throw new InvalidOperationException($"The embedding has dimension {vector.Length}, expected {Info.Dimensions}.");
                }
                vectors.Add(Normalize(vector));
            }
        }
        return vectors;
    }

    private static float[] Normalize(float[] vector)
    {
        var length = MathF.Sqrt(vector.Sum(x => x * x));
        return length == 0 ? vector : vector.Select(x => x / length).ToArray();
    }
}
