using AudioTranscription.Domain.Enums;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace AudioTranscription.Infrastructure.Services;

/// <summary>
/// Generates on-demand transcript variants (S10) via a local Ollama model: builds the prompt for the
/// mode (T1), chunks long transcripts for Summary/ActionItems with Map-Reduce (T4), and retries
/// transient failures (T4 resilience requirement in CLAUDE.md).
/// </summary>
public class OllamaVariantGenerator(
    IChatClient chatClient,
    IPostProcessingPromptCatalog prompts,
    IOptions<PostProcessingOptions> options,
    ILogger<OllamaVariantGenerator> logger) : IVariantGenerator
{
    /// <summary>
    /// Retries a transient failure (e.g. Ollama briefly unreachable) 2 times with a short exponential
    /// backoff. A caller's own cancellation is never retried: Polly detects that the ambient
    /// CancellationToken passed to ExecuteAsync is set and stops regardless of ShouldHandle.
    /// </summary>
    private static readonly ResiliencePipeline<ChatResponse> RetryPipeline = new ResiliencePipelineBuilder<ChatResponse>()
        .AddRetry(new RetryStrategyOptions<ChatResponse>
        {
            MaxRetryAttempts = 2,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(50),
            ShouldHandle = new PredicateBuilder<ChatResponse>().Handle<Exception>(),
        })
        .Build();

    private static readonly HashSet<PostProcessingMode> ChunkableModes = [PostProcessingMode.Summary, PostProcessingMode.ActionItems];

    public async Task<string> GenerateAsync(PostProcessingMode mode, string transcript, string? targetLanguage, CancellationToken cancellationToken = default)
    {
        if (ChunkableModes.Contains(mode))
        {
            var chunks = TranscriptChunker.Chunk(transcript, options.Value.MaxChunkLength);
            if (chunks.Count > 1)
            {
                logger.LogInformation("Generating {Mode} in {ChunkCount} chunks (Map-Reduce)", mode, chunks.Count);
                return await GenerateChunkedAsync(mode, chunks, cancellationToken);
            }
        }

        return await CallModelAsync(prompts.ForFullTranscript(mode, transcript, targetLanguage), cancellationToken);
    }

    private async Task<string> GenerateChunkedAsync(PostProcessingMode mode, IReadOnlyList<string> chunks, CancellationToken cancellationToken)
    {
        var partials = new List<string>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
            partials.Add(await CallModelAsync(prompts.ForChunk(mode, chunks[i], i, chunks.Count), cancellationToken));

        return await CallModelAsync(prompts.ForReduce(mode, partials), cancellationToken);
    }

    private async Task<string> CallModelAsync(PostProcessingPrompt prompt, CancellationToken cancellationToken)
    {
        ChatMessage[] messages =
        [
            new ChatMessage(ChatRole.System, prompt.SystemMessage),
            new ChatMessage(ChatRole.User, prompt.UserMessage),
        ];

        var response = await RetryPipeline.ExecuteAsync(
            async token => await chatClient.GetResponseAsync(messages, cancellationToken: token),
            cancellationToken);

        var text = response.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Das Sprachmodell hat keinen Text zurückgegeben.");
        return text;
    }
}
