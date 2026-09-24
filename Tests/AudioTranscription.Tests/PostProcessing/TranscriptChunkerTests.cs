using AudioTranscription.Infrastructure.Services;
using FluentAssertions;

namespace AudioTranscription.Tests.PostProcessing;

public class TranscriptChunkerTests
{
    [Fact]
    public void Chunk_ShortText_ReturnsOneChunk()
    {
        var chunks = TranscriptChunker.Chunk("Kurzer Text.", maxChunkLength: 100);

        chunks.Should().Equal("Kurzer Text.");
    }

    [Fact]
    public void Chunk_LongText_SplitsAtParagraphBreaksWithinTheLimit()
    {
        var text = string.Concat(Enumerable.Repeat("Ein Absatz mit etwas Inhalt.", 3).Select((p, i) => p + "\n\n"));
        // Three ~29-char paragraphs; a limit of 65 fits two but not three per chunk
        var chunks = TranscriptChunker.Chunk(text, maxChunkLength: 65);

        chunks.Should().HaveCountGreaterThan(1);
        chunks.Should().AllSatisfy(c => c.Length.Should().BeLessThanOrEqualTo(65));
        string.Join(" ", chunks).Replace("\n\n", " ").Should().Contain("Ein Absatz mit etwas Inhalt.");
    }

    [Fact]
    public void Chunk_NeverSplitsAWordInHalf()
    {
        var text = string.Join(" ", Enumerable.Range(0, 50).Select(i => $"Wort{i}"));

        var chunks = TranscriptChunker.Chunk(text, maxChunkLength: 30);

        var words = string.Join(" ", chunks).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        words.Should().Equal(text.Split(' '));
    }

    [Fact]
    public void Chunk_WordLongerThanTheLimit_KeptWholeOnItsOwnChunk()
    {
        var longWord = new string('x', 200);

        var chunks = TranscriptChunker.Chunk($"kurz {longWord} kurz", maxChunkLength: 50);

        chunks.Should().Contain(longWord);
    }

    [Fact]
    public void Chunk_EmptyOrWhitespaceText_ReturnsNoChunks()
    {
        TranscriptChunker.Chunk("", 100).Should().BeEmpty();
        TranscriptChunker.Chunk("   \n  ", 100).Should().BeEmpty();
    }

    [Fact]
    public void Chunk_TextExactlyAtTheLimit_ReturnsOneChunk()
    {
        var text = new string('a', 50);

        TranscriptChunker.Chunk(text, maxChunkLength: 50).Should().Equal(text);
    }
}
