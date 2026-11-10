using System.ClientModel;
using AudioTranscription.Api.Configuration;
using AudioTranscription.Infrastructure.Embeddings;
using Microsoft.Extensions.AI;
using OpenAI;
using Qdrant.Client;

namespace AudioTranscription.Api.Search;

/// <summary>
/// Registers semantic search (S23, ADR 0010) when a Qdrant connection string named "qdrant" exists. The embedding
/// model is local (Ollama) unless <c>Embedding:Provider</c> says OpenAI; without a usable embedding configuration
/// nothing is registered and the endpoints answer 503.
/// </summary>
public static class SemanticSearchSetup
{
    public const string QdrantConnectionName = "qdrant";

    public static bool AddSemanticSearch(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<EmbeddingOptions>(builder.Configuration.GetSection(EmbeddingOptions.SectionName));
        if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString(QdrantConnectionName)))
            return false;

        var options = builder.Configuration.GetSection(EmbeddingOptions.SectionName).Get<EmbeddingOptions>() ?? new EmbeddingOptions();
        var generator = CreateGenerator(builder.Configuration, options);
        if (generator is null)
            return false;

        builder.AddQdrantClient(QdrantConnectionName);
        builder.Services.AddSingleton(generator);
        builder.Services.AddSingleton<IEmbeddingService>(sp => new GeneratorEmbeddingService(
            sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
            new EmbeddingModelInfo(options.Provider, options.Model, options.Dimensions),
            sp.GetRequiredService<ILogger<GeneratorEmbeddingService>>()));
        builder.Services.AddSingleton<IVectorIndex>(sp => new QdrantVectorIndex(
            sp.GetRequiredService<QdrantClient>(), sp.GetRequiredService<ILogger<QdrantVectorIndex>>()));
        builder.Services.AddScoped<SemanticIndexer>();
        builder.Services.AddSingleton<IndexingQueue>();
        builder.Services.AddSingleton<ReindexProgress>();
        builder.Services.AddHostedService<IndexingWorker>();
        builder.Services.AddHealthChecks().AddCheck<QdrantHealthCheck>("qdrant", tags: ["ready"]);
        return true;
    }

    private static IEmbeddingGenerator<string, Embedding<float>>? CreateGenerator(IConfiguration configuration, EmbeddingOptions options)
    {
        if (string.Equals(options.Provider, EmbeddingOptions.OpenAiProvider, StringComparison.OrdinalIgnoreCase))
        {
            // Opt-in: transcript text leaves this machine only with an explicit key
            if (string.IsNullOrWhiteSpace(options.ApiKey))
                return null;
            var clientOptions = new OpenAIClientOptions();
            if (!string.IsNullOrWhiteSpace(options.Endpoint))
                clientOptions.Endpoint = new Uri(options.Endpoint);
            return new OpenAIClient(new ApiKeyCredential(options.ApiKey), clientOptions)
                .GetEmbeddingClient(options.Model)
                .AsIEmbeddingGenerator(options.Dimensions);
        }

        // Local Ollama: own endpoint, or the one of the chat model (same server), or the model's own connection string (Aspire)
        var endpoint = options.Endpoint
            ?? configuration.GetConnectionString(options.Model)
            ?? configuration.GetConnectionString("llama3.2");
        return string.IsNullOrWhiteSpace(endpoint) ? null : new OllamaEmbeddingGenerator(new Uri(endpoint), options.Model);
    }
}

/// <summary>Readiness check of the vector database (S23).</summary>
public class QdrantHealthCheck(IVectorIndex index) : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await index.IsHealthyAsync(cancellationToken)
            ? Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy()
            : Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Qdrant is not reachable.");
}
