using System.Runtime.CompilerServices;
using AudioTranscription.Domain.Chat;
using AudioTranscription.Infrastructure.Embeddings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace AudioTranscription.Api.Chat;

/// <summary>Configuration section "Chat" (S24, ADR 0011).</summary>
public class ChatSettings
{
    public const string SectionName = "Chat";
    public const string OllamaProvider = "Ollama";
    public const string OpenAiProvider = "OpenAI";

    /// <summary>Local Ollama unless OpenAI is chosen explicitly (and an API key is set).</summary>
    public string Provider { get; set; } = OllamaProvider;
    public string Model { get; set; } = "llama3.2";
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    /// <summary>Hits less similar than this are not used; without any hit the model is not asked.</summary>
    public double MinScore { get; set; } = 0.3;
    public int MaxHits { get; set; } = 8;
    public int MaxContextChars { get; set; } = 6_000;
    public int RequestsPerMinute { get; set; } = 20;
}

public abstract record ChatEvent;
/// <summary>A piece of the answer, as the model produces it.</summary>
public sealed record ChatTextEvent(string Text) : ChatEvent;
/// <summary>The finished answer with the citations that point at delivered sources.</summary>
public sealed record ChatDoneEvent(string Text, IReadOnlyList<ChatCitation> Citations) : ChatEvent;
/// <summary>The answer could not be produced; the message is fixed and free of technical details.</summary>
public sealed record ChatErrorEvent(string Message) : ChatEvent;

/// <summary>
/// Answers a question from the user's transcripts (S24, ADR 0011): retrieval over the allowed jobs, a prompt that keeps the
/// transcript apart from the instructions, a streamed answer, and citations that can only name delivered sources.
/// </summary>
public class ChatService(
    IEmbeddingService embeddings,
    IVectorIndex index,
    IChatClient chatClient,
    IOptions<ChatSettings> options,
    ILogger<ChatService> logger)
{
    public const string UnavailableMessage = "Die Antwort konnte nicht erzeugt werden. Ist das Sprachmodell erreichbar?";

    private static readonly ResiliencePipeline RetryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 2,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(50),
            ShouldHandle = new PredicateBuilder().Handle<Exception>(ex => ex is not OperationCanceledException),
        })
        .Build();

    public async IAsyncEnumerable<ChatEvent> AnswerAsync(
        string question,
        IReadOnlyCollection<Guid> allowedJobIds,
        IReadOnlyList<PromptMessage> history,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var settings = options.Value;

        IReadOnlyList<ChatSource> sources;
        ChatEvent? failure = null;
        try
        {
            sources = await RetrieveAsync(question, allowedJobIds, settings, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Retrieval for a chat question failed");
            sources = [];
            failure = new ChatErrorEvent(UnavailableMessage);
        }
        if (failure is not null)
        {
            yield return failure;
            yield break;
        }

        if (sources.Count == 0)
        {
            yield return new ChatDoneEvent(ChatPrompt.NoAnswer, []);
            yield break;
        }

        var prompt = ChatPrompt.Build(sources, question, history);
        var messages = new List<ChatMessage> { new(ChatRole.System, prompt.System) };
        messages.AddRange(prompt.Messages.Select(m => new ChatMessage(m.Role == "assistant" ? ChatRole.Assistant : ChatRole.User, m.Text)));

        var answer = new System.Text.StringBuilder();
        var attempts = 0;
        while (true)
        {
            attempts++;
            var stream = chatClient.GetStreamingResponseAsync(messages, cancellationToken: cancellationToken).GetAsyncEnumerator(cancellationToken);
            var started = false;
            Exception? error = null;
            try
            {
                while (true)
                {
                    string? piece = null;
                    bool hasNext;
                    try
                    {
                        hasNext = await stream.MoveNextAsync();
                        if (hasNext)
                            piece = stream.Current.Text;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        error = ex;
                        break;
                    }
                    if (!hasNext)
                        break;
                    if (string.IsNullOrEmpty(piece))
                        continue;
                    started = true;
                    answer.Append(piece);
                    yield return new ChatTextEvent(piece);
                }
            }
            finally
            {
                await stream.DisposeAsync();
            }

            if (error is null)
                break;

            // Only a failure before the first word is retried: afterwards the user has already seen text
            logger.LogWarning(error, "The language model failed (attempt {Attempt})", attempts);
            if (started || attempts > 2)
            {
                yield return new ChatErrorEvent(UnavailableMessage);
                yield break;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50 * attempts), cancellationToken);
        }

        var cited = ChatCitations.Extract(answer.ToString(), sources);
        yield return new ChatDoneEvent(cited.Text, cited.Citations);
    }

    private async Task<IReadOnlyList<ChatSource>> RetrieveAsync(
        string question, IReadOnlyCollection<Guid> allowedJobIds, ChatSettings settings, CancellationToken cancellationToken)
    {
        if (allowedJobIds.Count == 0 || string.IsNullOrWhiteSpace(question))
            return [];

        var vector = (await embeddings.EmbedAsync([question], cancellationToken))[0];
        var hits = await index.SearchAsync(vector, allowedJobIds, settings.MaxHits, 0, cancellationToken);
        return ChatContext.Build(
            hits.Where(h => h.Score >= settings.MinScore).Select(h => new ChatHit(h.JobId, h.StartMs, h.EndMs, h.Text, h.Score)).ToList(),
            settings.MaxContextChars);
    }
}
