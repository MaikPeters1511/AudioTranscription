using AudioTranscription.Infrastructure.Services;
using FluentAssertions;

namespace AudioTranscription.Tests.Transcription;

public class TranscriptionResultBuilderTests
{
    [Fact]
    public void Build_PassesSegmentsThroughUnchanged()
    {
        var builder = new TranscriptionResultBuilder();
        builder.Add(TimeSpan.Zero, TimeSpan.FromMilliseconds(1530), " Hallo zusammen.", "de");
        builder.Add(TimeSpan.FromMilliseconds(1530), TimeSpan.FromMilliseconds(3999), " Wie geht's?", "de");
        builder.Add(TimeSpan.FromHours(1.5), TimeSpan.FromHours(1.5) + TimeSpan.FromMilliseconds(1), " Ende", null);

        var result = builder.Build(requestedLanguage: null);

        result.Segments.Should().Equal(
            new SegmentResult(TimeSpan.Zero, TimeSpan.FromMilliseconds(1530), "Hallo zusammen."),
            new SegmentResult(TimeSpan.FromMilliseconds(1530), TimeSpan.FromMilliseconds(3999), "Wie geht's?"),
            new SegmentResult(TimeSpan.FromHours(1.5), TimeSpan.FromHours(1.5) + TimeSpan.FromMilliseconds(1), "Ende"));
        result.Text.Should().Be("Hallo zusammen. Wie geht's? Ende");
        result.DurationSeconds.Should().Be(5400.001);
        result.DetectedLanguage.Should().Be("de");
    }

    [Fact]
    public void Build_WithoutDetectedLanguage_UsesRequestedLanguage()
    {
        var builder = new TranscriptionResultBuilder();
        builder.Add(TimeSpan.Zero, TimeSpan.FromSeconds(1), "Hi", null);

        builder.Build(requestedLanguage: "en").DetectedLanguage.Should().Be("en");
    }

    [Fact]
    public void Build_WithoutSegments_ReturnsEmptyResult()
    {
        var result = new TranscriptionResultBuilder().Build(requestedLanguage: null);

        result.Text.Should().BeEmpty();
        result.Segments.Should().BeEmpty();
        result.DurationSeconds.Should().BeNull();
        result.DetectedLanguage.Should().BeNull();
    }
}
