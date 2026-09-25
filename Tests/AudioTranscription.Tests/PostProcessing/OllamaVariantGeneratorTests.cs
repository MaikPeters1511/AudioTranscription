using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AudioTranscription.Tests.PostProcessing;

public class OllamaVariantGeneratorTests
{
    private readonly Mock<IChatClient> _chatClient = new();

    private OllamaVariantGenerator CreateSut(int maxChunkLength = 6000) =>
        new(_chatClient.Object, new PostProcessingPromptCatalog(),
            Options.Create(new PostProcessingOptions { MaxChunkLength = maxChunkLength }),
            NullLogger<OllamaVariantGenerator>.Instance);

    private void RespondWith(params string[] responses)
    {
        var queue = new Queue<string>(responses);
        _chatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ChatResponse(new ChatMessage(ChatRole.Assistant, queue.Dequeue())));
    }

    [Fact]
    public async Task GenerateAsync_ShortTranscript_MakesOneCallAndReturnsItsResult()
    {
        RespondWith("Aufgeräumter Text.");

        var result = await CreateSut().GenerateAsync(PostProcessingMode.Cleanup, "roher text", targetLanguage: null);

        result.Should().Be("Aufgeräumter Text.");
        _chatClient.Verify(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateAsync_LongSummary_ChunksThenReduces()
    {
        // Long enough that TranscriptChunker (maxChunkLength: 100) splits it into several chunks;
        // the exact count is an implementation detail, so the mock replies based on the prompt shape instead.
        var longTranscript = string.Join(" ", Enumerable.Range(0, 20).Select(i => $"Satz Nummer {i} mit etwas Inhalt."));
        var chunkCalls = 0;
        _chatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns<IEnumerable<ChatMessage>, ChatOptions, CancellationToken>((messages, _, _) =>
            {
                var isChunkCall = messages.Last().Text.StartsWith("Excerpt:");
                var reply = isChunkCall ? $"Teilzusammenfassung {++chunkCalls}" : "Gesamtzusammenfassung";
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
            });

        var result = await CreateSut(maxChunkLength: 100).GenerateAsync(PostProcessingMode.Summary, longTranscript, targetLanguage: null);

        result.Should().Be("Gesamtzusammenfassung");
        chunkCalls.Should().BeGreaterThan(1, "the transcript is long enough to be split into several chunks");
        _chatClient.Verify(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(chunkCalls + 1));
    }

    [Fact]
    public async Task GenerateAsync_ShortSummary_MakesOneCallWithoutMapReduce()
    {
        RespondWith("Kurzzusammenfassung");

        var result = await CreateSut(maxChunkLength: 6000).GenerateAsync(PostProcessingMode.Summary, "kurzer text", targetLanguage: null);

        result.Should().Be("Kurzzusammenfassung");
        // A single chunk must use the plain full-transcript prompt, not a one-chunk Map-Reduce round trip
        _chatClient.Verify(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateAsync_LongCleanup_IsNeverChunked()
    {
        var longTranscript = string.Join(" ", Enumerable.Range(0, 20).Select(i => $"Satz Nummer {i}."));
        RespondWith("Aufgeräumt");

        var result = await CreateSut(maxChunkLength: 50).GenerateAsync(PostProcessingMode.Cleanup, longTranscript, targetLanguage: null);

        result.Should().Be("Aufgeräumt");
        _chatClient.Verify(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateAsync_TransientFailureThenSuccess_RetriesAndReturnsResult()
    {
        var calls = 0;
        _chatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                calls++;
                return calls < 3
                    ? Task.FromException<ChatResponse>(new HttpRequestException("Ollama unreachable"))
                    : Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Ergebnis")));
            });

        var result = await CreateSut().GenerateAsync(PostProcessingMode.Cleanup, "text", targetLanguage: null);

        result.Should().Be("Ergebnis");
        calls.Should().Be(3);
    }

    [Fact]
    public async Task GenerateAsync_PersistentFailure_ThrowsAfterExhaustingRetries()
    {
        _chatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Ollama unreachable"));

        var act = () => CreateSut().GenerateAsync(PostProcessingMode.Cleanup, "text", targetLanguage: null);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GenerateAsync_WhenCancelled_DoesNotRetry()
    {
        using var cts = new CancellationTokenSource();
        var calls = 0;
        _chatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns(() => { calls++; cts.Cancel(); throw new OperationCanceledException(cts.Token); });

        var act = () => CreateSut().GenerateAsync(PostProcessingMode.Cleanup, "text", targetLanguage: null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task GenerateAsync_EmptyResponse_ThrowsInsteadOfSilentlySucceeding()
    {
        RespondWith("   ");

        var act = () => CreateSut().GenerateAsync(PostProcessingMode.Cleanup, "text", targetLanguage: null);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
