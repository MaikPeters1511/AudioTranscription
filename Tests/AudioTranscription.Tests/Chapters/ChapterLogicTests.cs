using AudioTranscription.Domain.Chapters;
using FluentAssertions;

namespace AudioTranscription.Tests.Chapters;

/// <summary>S25-T1: windowing, parsing the LLM answer, merging and turning drafts into chapters on segment boundaries.</summary>
public class ChapterLogicTests
{
    private static List<ChapterSegment> Segments(int count, int lengthMs = 5_000, string text = "Wort Wort Wort") =>
        Enumerable.Range(0, count)
            .Select(i => new ChapterSegment(i, i * (long)lengthMs, (i + 1) * (long)lengthMs, $"{text} {i}"))
            .ToList();

    // --- windowing ---------------------------------------------------------------

    [Fact]
    public void Windowing_ShortTranscript_IsOneWindow()
    {
        var windows = ChapterWindowing.Split(Segments(5), maxChars: 10_000);

        windows.Should().ContainSingle().Which.Should().HaveCount(5);
    }

    [Fact]
    public void Windowing_LongTranscript_SplitsOnSegmentBoundaries_WithoutLosingOrRepeatingAnything()
    {
        var segments = Segments(100);

        var windows = ChapterWindowing.Split(segments, maxChars: 300);

        windows.Count.Should().BeGreaterThan(1);
        windows.SelectMany(w => w).Select(s => s.Index).Should().Equal(segments.Select(s => s.Index));
        windows.Should().OnlyContain(w => w.Count > 0);
    }

    [Fact]
    public void Windowing_ASegmentLongerThanTheLimit_GetsItsOwnWindow()
    {
        var segments = new List<ChapterSegment>
        {
            new(0, 0, 1_000, "kurz"),
            new(1, 1_000, 2_000, new string('x', 500)),
            new(2, 2_000, 3_000, "kurz"),
        };

        var windows = ChapterWindowing.Split(segments, maxChars: 100);

        windows.SelectMany(w => w).Should().HaveCount(3);
        windows.Single(w => w.Any(s => s.Index == 1)).Should().HaveCount(1);
    }

    [Fact]
    public void Windowing_NoSegments_NoWindows() =>
        ChapterWindowing.Split([], 100).Should().BeEmpty();

    // --- parsing -----------------------------------------------------------------

    [Fact]
    public void Parse_ReadsAJsonArray()
    {
        var drafts = ChapterResponseParser.Parse(
            """[{"startSegment":0,"title":"Begrüßung","summary":"Alle werden begrüßt."},{"startSegment":7,"title":"Budget","summary":"Zahlen."}]""",
            minIndex: 0, maxIndex: 20);

        drafts.Should().Equal(
            new ChapterDraft(0, "Begrüßung", "Alle werden begrüßt."),
            new ChapterDraft(7, "Budget", "Zahlen."));
    }

    [Fact]
    public void Parse_ToleratesCodeFencesAndTextAroundTheArray()
    {
        var response = "Hier sind die Kapitel:\n```json\n[{\"startSegment\": 3, \"title\": \"Thema\", \"summary\": \"Inhalt\"}]\n```\nViel Erfolg!";

        ChapterResponseParser.Parse(response, 0, 10).Should().ContainSingle().Which.StartSegmentIndex.Should().Be(3);
    }

    [Fact]
    public void Parse_AcceptsAnObjectWithAChaptersArray_AndAnyPropertyCasing()
    {
        var response = """{"chapters":[{"StartSegment":2,"Title":"Titel","Summary":"Text"}]}""";

        ChapterResponseParser.Parse(response, 0, 10).Should().ContainSingle().Which.Title.Should().Be("Titel");
    }

    [Fact]
    public void Parse_SkipsEntriesOutsideTheWindowOrWithoutATitle()
    {
        var response = """
            [{"startSegment":99,"title":"Zu weit","summary":""},
             {"startSegment":-1,"title":"Negativ","summary":""},
             {"startSegment":4,"title":"  ","summary":"ohne Titel"},
             {"startSegment":5,"title":"Gültig"}]
            """;

        var drafts = ChapterResponseParser.Parse(response, minIndex: 0, maxIndex: 10);

        drafts.Should().ContainSingle().Which.Should().Be(new ChapterDraft(5, "Gültig", ""));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Das kann ich leider nicht.")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{\"foo\": 1}")]
    [InlineData("[{\"startSegment\": 3, \"title\": ")]
    public void Parse_UnusableAnswer_ThrowsAChapterParseException(string response)
    {
        var act = () => ChapterResponseParser.Parse(response, 0, 10);

        act.Should().Throw<ChapterParseException>();
    }

    [Fact]
    public void Parse_CutsOverlongTitlesAndSummaries()
    {
        var response = $$"""[{"startSegment":0,"title":"{{new string('t', 500)}}","summary":"{{new string('s', 5000)}}"}]""";

        var draft = ChapterResponseParser.Parse(response, 0, 5).Single();

        draft.Title.Length.Should().BeLessThanOrEqualTo(200);
        draft.Summary.Length.Should().BeLessThanOrEqualTo(1_000);
    }

    // --- merging -----------------------------------------------------------------

    [Fact]
    public void Merge_SortsDropsDuplicateStarts_AndStartsAtTheFirstSegment()
    {
        var merged = ChapterMerger.Merge(
        [
            new ChapterDraft(10, "B", "b"),
            new ChapterDraft(3, "A", "a"),
            new ChapterDraft(10, "B doppelt", "x"),
        ], firstIndex: 0, lastIndex: 20);

        merged.Select(d => (d.StartSegmentIndex, d.Title)).Should().Equal((0, "A"), (10, "B"));
    }

    [Fact]
    public void Merge_JoinsNeighboursWithTheSameTitle_AcrossWindows()
    {
        var merged = ChapterMerger.Merge(
        [
            new ChapterDraft(0, "Budget", "Teil 1"),
            new ChapterDraft(10, "budget ", "Teil 2"),
            new ChapterDraft(20, "Ausblick", "z"),
        ], 0, 30);

        merged.Select(d => d.Title).Should().Equal("Budget", "Ausblick");
        merged[0].Summary.Should().Be("Teil 1");
    }

    [Fact]
    public void Merge_NoDrafts_GivesOneChapterForTheWholeRecording()
    {
        var merged = ChapterMerger.Merge([], 0, 9);

        merged.Should().ContainSingle().Which.StartSegmentIndex.Should().Be(0);
    }

    // --- building chapters --------------------------------------------------------

    [Fact]
    public void Build_PutsBoundariesOnSegmentBoundaries_WithoutGapsOrOverlap()
    {
        var segments = Segments(12, lengthMs: 5_000);
        var jobId = Guid.NewGuid();

        var chapters = ChapterBuilder.Build(jobId, segments,
        [
            new ChapterDraft(0, "A", "a"),
            new ChapterDraft(4, "B", "b"),
            new ChapterDraft(9, "C", "c"),
        ]);

        chapters.Select(c => (c.StartMs, c.EndMs)).Should().Equal((0L, 20_000L), (20_000L, 45_000L), (45_000L, 60_000L));
        chapters.Select(c => c.Position).Should().Equal(0, 1, 2);
        chapters.Should().OnlyContain(c => c.AudioJobId == jobId);
    }

    [Fact]
    public void Build_IgnoresDraftsPointingAtUnknownSegments()
    {
        var chapters = ChapterBuilder.Build(Guid.NewGuid(), Segments(3), [new ChapterDraft(0, "A", ""), new ChapterDraft(50, "Weg", "")]);

        chapters.Should().ContainSingle().Which.EndMs.Should().Be(15_000);
    }
}
