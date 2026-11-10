using AudioTranscription.Domain.Search;
using FluentAssertions;

namespace AudioTranscription.Tests.Search;

/// <summary>S23: chunking of segments for the vector index, stable chunk ids and the fusion of both result lists.</summary>
public class SemanticLogicTests
{
    private static List<TimedText> Segments(int count, long lengthMs = 5_000, string text = "Das ist ein Satz mit etwas Inhalt.") =>
        Enumerable.Range(0, count).Select(i => new TimedText(i * lengthMs, (i + 1) * lengthMs, $"{text} {i}")).ToList();

    // --- chunking ----------------------------------------------------------------

    [Fact]
    public void Chunk_ShortRecording_IsOneChunk()
    {
        var chunks = SegmentChunker.Build(Segments(4));

        chunks.Should().ContainSingle();
        chunks[0].StartMs.Should().Be(0);
        chunks[0].EndMs.Should().Be(20_000);
        chunks[0].Text.Should().Contain("Satz mit etwas Inhalt. 0").And.Contain("Satz mit etwas Inhalt. 3");
    }

    [Fact]
    public void Chunk_LongRecording_GivesOverlappingChunksOfAboutTheTargetLength()
    {
        var segments = Segments(120); // ten minutes

        var chunks = SegmentChunker.Build(segments, targetMs: 45_000, overlapMs: 15_000);

        chunks.Count.Should().BeGreaterThan(10);
        chunks.Should().OnlyContain(c => c.EndMs - c.StartMs <= 45_000 + 5_000, "a chunk stops once the target length is reached");
        for (var i = 1; i < chunks.Count; i++)
        {
            chunks[i].StartMs.Should().BeGreaterThan(chunks[i - 1].StartMs, "every chunk moves forward");
            chunks[i].StartMs.Should().BeLessThan(chunks[i - 1].EndMs, "neighbouring chunks overlap");
        }
        chunks[0].StartMs.Should().Be(0);
        chunks[^1].EndMs.Should().Be(600_000);
    }

    [Fact]
    public void Chunk_CoversEverySegment()
    {
        var segments = Segments(80);

        var chunks = SegmentChunker.Build(segments);

        foreach (var segment in segments)
            chunks.Should().Contain(c => c.StartMs <= segment.StartMs && c.EndMs >= segment.EndMs, $"segment at {segment.StartMs} belongs to a chunk");
    }

    [Fact]
    public void Chunk_NumbersTheChunksFromZero() =>
        SegmentChunker.Build(Segments(60)).Select(c => c.Index).Should().Equal(Enumerable.Range(0, SegmentChunker.Build(Segments(60)).Count));

    [Fact]
    public void Chunk_SkipsEmptySegments_AndNothingMeansNoChunks()
    {
        var segments = new List<TimedText> { new(0, 1_000, "  "), new(1_000, 2_000, "Hallo"), new(2_000, 3_000, "") };

        var chunks = SegmentChunker.Build(segments);

        chunks.Should().ContainSingle().Which.Text.Should().Be("Hallo");
        SegmentChunker.Build([]).Should().BeEmpty();
    }

    [Fact]
    public void Chunk_RespectsTheCharacterLimit_AlsoForOneHugeSegment()
    {
        var huge = new List<TimedText> { new(0, 60_000, string.Join(" ", Enumerable.Repeat("wort", 1_000))) };

        var chunks = SegmentChunker.Build(huge, maxChars: 500);

        chunks.Count.Should().BeGreaterThan(1);
        chunks.Should().OnlyContain(c => c.Text.Length <= 500);
        chunks.Should().OnlyContain(c => c.StartMs == 0 && c.EndMs == 60_000);
    }

    [Fact]
    public void Chunk_NeverCutsInsideAWord()
    {
        var huge = new List<TimedText> { new(0, 10_000, string.Join(" ", Enumerable.Repeat("Donaudampfschiff", 100))) };

        var chunks = SegmentChunker.Build(huge, maxChars: 100);

        chunks.Should().OnlyContain(c => c.Text.Split(' ').All(w => w == "Donaudampfschiff"));
    }

    // --- ids -----------------------------------------------------------------------

    [Fact]
    public void ChunkId_IsStable_AndDiffersPerJobAndPosition()
    {
        var job = Guid.NewGuid();

        ChunkId.For(job, 3).Should().Be(ChunkId.For(job, 3));
        ChunkId.For(job, 3).Should().NotBe(ChunkId.For(job, 4));
        ChunkId.For(job, 3).Should().NotBe(ChunkId.For(Guid.NewGuid(), 3));
    }

    // --- fusion ----------------------------------------------------------------------

    private static RankedHit Hit(Guid job, long? start, long? end = null, string text = "t") => new(job, start, end ?? start, text);

    [Fact]
    public void Fuse_RanksHitsFoundByBothListsFirst()
    {
        var job = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var fulltext = new[] { Hit(a, 5_000), Hit(job, 12_000) };
        var semantic = new[] { Hit(b, 0, 30_000), Hit(job, 0, 30_000) };

        var fused = ReciprocalRankFusion.Fuse(fulltext, semantic);

        fused[0].JobId.Should().Be(job, "the hit in the full-text and the vector results has the best combined rank");
        fused.Select(f => f.JobId).Should().BeEquivalentTo([job, a, b]);
    }

    [Fact]
    public void Fuse_MergesAFullTextHitIntoTheSemanticChunkItLiesIn()
    {
        var job = Guid.NewGuid();

        var fused = ReciprocalRankFusion.Fuse([Hit(job, 12_000)], [Hit(job, 0, 30_000), Hit(job, 60_000, 90_000)]);

        fused.Should().HaveCount(2);
        fused[0].FullText.Should().NotBeNull();
        fused[0].Semantic!.StartMs.Should().Be(0);
        fused[1].FullText.Should().BeNull();
    }

    [Fact]
    public void Fuse_KeepsHitsOfOneListWhenTheOtherIsEmpty()
    {
        var job = Guid.NewGuid();

        ReciprocalRankFusion.Fuse([Hit(job, 1_000)], []).Should().ContainSingle();
        ReciprocalRankFusion.Fuse([], [Hit(job, 0, 30_000)]).Should().ContainSingle();
        ReciprocalRankFusion.Fuse([], []).Should().BeEmpty();
    }

    [Fact]
    public void Fuse_JobLevelFullTextHitsStaySeparate()
    {
        var job = Guid.NewGuid();

        var fused = ReciprocalRankFusion.Fuse([Hit(job, null)], [Hit(job, 0, 30_000)]);

        fused.Should().HaveCount(2);
    }

    [Fact]
    public void Fuse_ScoreFollowsTheRankFormula()
    {
        var job = Guid.NewGuid();

        var fused = ReciprocalRankFusion.Fuse([Hit(job, 5_000)], [Hit(job, 0, 30_000)]);

        fused[0].Score.Should().BeApproximately(2.0 / (ReciprocalRankFusion.K + 1), 1e-9);
    }
}
