using AudioTranscription.Domain.Glossary;
using FluentAssertions;

namespace AudioTranscription.Tests.Glossary;

/// <summary>S22: the Whisper initial prompt built from glossary terms.</summary>
public class GlossaryPromptTests
{
    [Fact]
    public void Build_JoinsTermsWithCommas()
    {
        GlossaryPrompt.Build(["Kubernetes", "Müller GmbH", "OAuth"]).Should().Be("Kubernetes, Müller GmbH, OAuth");
    }

    [Fact]
    public void Build_WithoutTerms_IsEmpty()
    {
        GlossaryPrompt.Build([]).Should().BeEmpty();
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("abc", 1)]
    [InlineData("abcd", 2)]
    public void EstimateTokens_IsAConservativeThreeCharactersPerToken(string text, int expected)
    {
        GlossaryPrompt.EstimateTokens(text).Should().Be(expected);
    }

    [Fact]
    public void Fits_UpToTheTokenLimit_AndNotBeyond()
    {
        var atLimit = new string('a', GlossaryPrompt.MaxTokens * 3);

        GlossaryPrompt.Fits([atLimit]).Should().BeTrue();
        GlossaryPrompt.Fits([atLimit + "a"]).Should().BeFalse();
    }
}
