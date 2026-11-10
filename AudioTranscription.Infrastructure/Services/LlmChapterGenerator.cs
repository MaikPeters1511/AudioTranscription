using AudioTranscription.Domain.Chapters;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace AudioTranscription.Infrastructure.Services;

/// <summary>Proposes chapters for a recording (S25).</summary>
public interface IChapterGenerator
{
    /// <exception cref="ChapterParseException">The model's answer could not be turned into chapters.</exception>
    /// <exception cref="Exception">The model could not be reached; the caller marks the run Failed.</exception>
    Task<IReadOnlyList<ChapterDraft>> GenerateAsync(IReadOnlyList<ChapterSegment> segments, CancellationToken cancellationToken = default);
}

/// <summary>
/// Chapter generation with a local LLM (S25): the numbered segments are cut into windows that fit the model
/// (Map), each window is asked for its chapters as JSON, and the answers are merged (Reduce) with
/// <see cref="ChapterMerger"/>. Transient failures are retried like in <see cref="OllamaVariantGenerator"/>.
/// </summary>
public class LlmChapterGenerator(
    IChatClient chatClient,
    IOptions<PostProcessingOptions> options,
    ILogger<LlmChapterGenerator> logger) : IChapterGenerator
{
    private static readonly ResiliencePipeline<ChatResponse> RetryPipeline = new ResiliencePipelineBuilder<ChatResponse>()
        .AddRetry(new RetryStrategyOptions<ChatResponse>
        {
            MaxRetryAttempts = 2,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(50),
            ShouldHandle = new PredicateBuilder<ChatResponse>().Handle<Exception>(ex => ex is not ChapterParseException),
        })
        .Build();

    private const string SystemMessage =
        "You split a transcript into chapters. The transcript is given as numbered segments, one per line, like \"[12] text\". " +
        "Divide it into thematic sections. Answer with JSON only: an array of objects with the fields " +
        "\"startSegment\" (the number of the segment where the chapter begins), \"title\" (a short heading) and " +
        "\"summary\" (one or two sentences). Order the chapters by startSegment, start the first chapter at the first " +
        "segment you were given, and use only segment numbers you were given. " +
        "Write titles and summaries in the language of the transcript.";

    public async Task<IReadOnlyList<ChapterDraft>> GenerateAsync(IReadOnlyList<ChapterSegment> segments, CancellationToken cancellationToken = default)
    {
        if (segments.Count == 0)
            return [];

        var windows = ChapterWindowing.Split(segments, options.Value.MaxChunkLength);
        logger.LogInformation("Generating chapters for {SegmentCount} segments in {WindowCount} window(s)", segments.Count, windows.Count);

        var drafts = new List<ChapterDraft>();
        foreach (var window in windows)
        {
            var answer = await AskAsync(window, cancellationToken);
            try
            {
                drafts.AddRange(ChapterResponseParser.Parse(answer, window[0].Index, window[^1].Index));
            }
            catch (ChapterParseException ex)
            {
                // One bad window must not sink an hour of recording: its segments join the previous chapter
                logger.LogWarning(ex, "Skipping a window of {SegmentCount} segments without usable chapters", window.Count);
            }
        }

        if (drafts.Count == 0)
            throw new ChapterParseException("The language model returned no usable chapter for this recording.");

        return ChapterMerger.Merge(drafts, segments[0].Index, segments[^1].Index);
    }

    private async Task<string> AskAsync(IReadOnlyList<ChapterSegment> window, CancellationToken cancellationToken)
    {
        ChatMessage[] messages =
        [
            new ChatMessage(ChatRole.System, SystemMessage),
            new ChatMessage(ChatRole.User, string.Join("\n", window.Select(ChapterWindowing.Line))),
        ];

        var response = await RetryPipeline.ExecuteAsync(
            async token => await chatClient.GetResponseAsync(messages, cancellationToken: token),
            cancellationToken);
        return response.Text ?? string.Empty;
    }
}
