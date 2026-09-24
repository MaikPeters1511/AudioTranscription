using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Services;
using FluentAssertions;

namespace AudioTranscription.Tests.PostProcessing;

public class PostProcessingPromptCatalogTests
{
    private readonly PostProcessingPromptCatalog _catalog = new();
    private const string Transcript = "Hallo zusammen, willkommen zum Quartalsmeeting.";

    [Theory]
    [InlineData(PostProcessingMode.Cleanup, "clean")]
    [InlineData(PostProcessingMode.Summary, "summar")]
    [InlineData(PostProcessingMode.BulletPoints, "bullet")]
    [InlineData(PostProcessingMode.ActionItems, "action item")]
    public void ForFullTranscript_ContainsTranscriptAndModeInstruction(PostProcessingMode mode, string instructionKeyword)
    {
        var prompt = _catalog.ForFullTranscript(mode, Transcript, targetLanguage: null);

        prompt.UserMessage.Should().Contain(Transcript);
        prompt.SystemMessage.ToLowerInvariant().Should().Contain(instructionKeyword);
    }

    [Fact]
    public void ForFullTranscript_Translate_ContainsTranscriptAndTargetLanguage()
    {
        var prompt = _catalog.ForFullTranscript(PostProcessingMode.Translate, Transcript, targetLanguage: "fr");

        prompt.UserMessage.Should().Contain(Transcript);
        prompt.SystemMessage.Should().Contain("fr");
    }

    [Fact]
    public void ForFullTranscript_Translate_WithoutTargetLanguage_Throws()
    {
        var act = () => _catalog.ForFullTranscript(PostProcessingMode.Translate, Transcript, targetLanguage: null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ForFullTranscript_NeverInventsInformationAndOnlyOutputsTheResult()
    {
        var prompt = _catalog.ForFullTranscript(PostProcessingMode.Summary, Transcript, targetLanguage: null);

        prompt.SystemMessage.ToLowerInvariant().Should().Contain("only output");
    }

    [Theory]
    [InlineData(PostProcessingMode.Summary)]
    [InlineData(PostProcessingMode.ActionItems)]
    public void ForChunk_ContainsTheChunkTextAndItsPosition(PostProcessingMode mode)
    {
        var prompt = _catalog.ForChunk(mode, "Teil zwei des Transkripts.", chunkIndex: 1, chunkCount: 3);

        prompt.UserMessage.Should().Contain("Teil zwei des Transkripts.");
        prompt.SystemMessage.Should().Contain("2").And.Contain("3"); // 1-based position of 3
    }

    [Theory]
    [InlineData(PostProcessingMode.Summary)]
    [InlineData(PostProcessingMode.ActionItems)]
    public void ForReduce_ContainsAllPartialResults(PostProcessingMode mode)
    {
        string[] partials = ["Erster Teil-Text.", "Zweiter Teil-Text."];

        var prompt = _catalog.ForReduce(mode, partials);

        prompt.UserMessage.Should().ContainAll(partials);
    }

    [Theory]
    [InlineData(PostProcessingMode.Cleanup)]
    [InlineData(PostProcessingMode.BulletPoints)]
    [InlineData(PostProcessingMode.Translate)]
    public void ForChunk_UnsupportedMode_Throws(PostProcessingMode mode)
    {
        var act = () => _catalog.ForChunk(mode, "x", 0, 1);

        act.Should().Throw<NotSupportedException>();
    }
}
