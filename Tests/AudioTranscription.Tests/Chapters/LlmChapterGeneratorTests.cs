using System.Text.RegularExpressions;
using AudioTranscription.Domain.Chapters;
using AudioTranscription.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AudioTranscription.Tests.Chapters;

/// <summary>S25-T1/T2: chapter generation with a fake LLM.</summary>
public partial class LlmChapterGeneratorTests
{
    private readonly Mock<IChatClient> _chatClient = new();

    private LlmChapterGenerator CreateSut(int maxChunkLength = 6000) =>
        new(_chatClient.Object, Options.Create(new PostProcessingOptions { MaxChunkLength = maxChunkLength }),
            NullLogger<LlmChapterGenerator>.Instance);

    private void Respond(Func<string, string> reply) =>
        _chatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns<IEnumerable<ChatMessage>, ChatOptions, CancellationToken>((messages, _, _) =>
                Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply(messages.Last().Text)))));

    private static List<ChapterSegment> Segments(int count) =>
        Enumerable.Range(0, count).Select(i => new ChapterSegment(i, i * 5_000L, (i + 1) * 5_000L, $"Satz Nummer {i} mit etwas Inhalt.")).ToList();

    /// <summary>A model that starts a chapter at every 10th segment of the window it was shown.</summary>
    private static string ChapterEveryTen(string prompt)
    {
        var indices = IndexPattern().Matches(prompt).Select(m => int.Parse(m.Groups[1].Value)).ToList();
        var starts = indices.Where(i => i % 10 == 0).Select(i => $$"""{"startSegment":{{i}},"title":"Thema {{i / 10}}","summary":"Zusammenfassung {{i / 10}}"}""");
        return "[" + string.Join(",", starts) + "]";
    }

    [GeneratedRegex(@"^\[(\d+)\]", RegexOptions.Multiline)]
    private static partial Regex IndexPattern();

    [Fact]
    public async Task Generate_ShortRecording_MakesOneCall()
    {
        Respond(_ => """[{"startSegment":0,"title":"Alles","summary":"Kurz."}]""");

        var drafts = await CreateSut().GenerateAsync(Segments(5));

        drafts.Should().ContainSingle().Which.Title.Should().Be("Alles");
        _chatClient.Verify(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Generate_LongRecording_AsksPerWindowAndMergesTheAnswers()
    {
        Respond(ChapterEveryTen);
        var segments = Segments(100);

        var drafts = await CreateSut(maxChunkLength: 600).GenerateAsync(segments);

        _chatClient.Invocations.Count.Should().BeGreaterThan(1, "the recording does not fit into one window");
        drafts.Select(d => d.StartSegmentIndex).Should().Equal(Enumerable.Range(0, 10).Select(i => i * 10));
    }

    [Fact]
    public async Task Generate_OneHourRecording_GivesChaptersWithoutGapsOrOverlap()
    {
        Respond(ChapterEveryTen);
        var segments = Segments(720); // 720 segments of 5 s = one hour

        var drafts = await CreateSut(maxChunkLength: 3000).GenerateAsync(segments);
        var chapters = ChapterBuilder.Build(Guid.NewGuid(), segments, drafts);

        chapters.Should().NotBeEmpty();
        chapters[0].StartMs.Should().Be(0);
        chapters[^1].EndMs.Should().Be(3_600_000);
        for (var i = 1; i < chapters.Count; i++)
            chapters[i].StartMs.Should().Be(chapters[i - 1].EndMs, "chapters follow each other without gap or overlap");
    }

    [Fact]
    public async Task Generate_BrokenAnswer_ThrowsAChapterParseException()
    {
        Respond(_ => "Leider kein JSON, aber ein netter Text.");

        var act = () => CreateSut().GenerateAsync(Segments(5));

        await act.Should().ThrowAsync<ChapterParseException>();
    }

    [Fact]
    public async Task Generate_TransientFailure_IsRetried()
    {
        var calls = 0;
        _chatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns(() => ++calls == 1
                ? throw new HttpRequestException("Ollama kurz weg")
                : Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, """[{"startSegment":0,"title":"A","summary":""}]"""))));

        var drafts = await CreateSut().GenerateAsync(Segments(3));

        drafts.Should().ContainSingle();
        calls.Should().Be(2);
    }

    [Fact]
    public async Task Generate_PromptAsksForJsonInTheLanguageOfTheTranscript()
    {
        IEnumerable<ChatMessage>? seen = null;
        _chatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions, CancellationToken>((m, _, _) => seen = m.ToList())
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, """[{"startSegment":0,"title":"A","summary":""}]""")));

        await CreateSut().GenerateAsync(Segments(3));

        var system = seen!.First(m => m.Role == ChatRole.System).Text;
        system.Should().Contain("JSON").And.Contain("startSegment").And.Contain("language of the transcript");
        seen!.Last().Text.Should().Contain("[0] Satz Nummer 0");
    }

    [Fact]
    public async Task Generate_NoSegments_NeedsNoModel()
    {
        var drafts = await CreateSut().GenerateAsync([]);

        drafts.Should().BeEmpty();
        _chatClient.Invocations.Should().BeEmpty();
    }
}
