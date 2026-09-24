using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Subtitles;
using FluentAssertions;

namespace AudioTranscription.Tests.Subtitles;

public class SubtitleFormatterTests
{
    private static TranscriptSegment Segment(int index, long startMs, long endMs, string text, int? speakerIndex = null) =>
        new() { Index = index, StartMs = startMs, EndMs = endMs, Text = text, SpeakerIndex = speakerIndex };

    private static readonly TranscriptSegment[] Meeting =
    [
        Segment(0, 0, 1_500, "Hallo zusammen."),
        Segment(1, 1_500, 4_250, "Heute besprechen wir den Quartalsbericht und die nächsten Schritte."),
    ];

    [Fact]
    public void ToSrt_FormatsNumberedCuesWithCommaMilliseconds() =>
        SubtitleFormatter.ToSrt(Meeting).Should().Be(
            """
            1
            00:00:00,000 --> 00:00:01,500
            Hallo zusammen.

            2
            00:00:01,500 --> 00:00:04,250
            Heute besprechen wir den Quartalsbericht
            und die nächsten Schritte.

            """.ReplaceLineEndings("\n"));

    [Fact]
    public void ToVtt_StartsWithHeaderAndUsesDotMilliseconds() =>
        SubtitleFormatter.ToVtt(Meeting).Should().Be(
            """
            WEBVTT

            00:00:00.000 --> 00:00:01.500
            Hallo zusammen.

            00:00:01.500 --> 00:00:04.250
            Heute besprechen wir den Quartalsbericht
            und die nächsten Schritte.

            """.ReplaceLineEndings("\n"));

    [Fact]
    public void Timestamps_BeyondOneHour_KeepCounting()
    {
        var segment = Segment(0, 3_723_004, 90_000_000, "Spät");

        SubtitleFormatter.ToSrt([segment]).Should().Contain("01:02:03,004 --> 25:00:00,000");
        SubtitleFormatter.ToVtt([segment]).Should().Contain("01:02:03.004 --> 25:00:00.000");
    }

    [Fact]
    public void EmptySegments_AreSkippedAndCuesRenumbered()
    {
        TranscriptSegment[] segments =
        [
            Segment(0, 0, 1_000, "Eins"),
            Segment(1, 1_000, 2_000, "   "),
            Segment(2, 2_000, 3_000, ""),
            Segment(3, 3_000, 4_000, "Zwei"),
        ];

        SubtitleFormatter.ToSrt(segments).Should().Be(
            "1\n00:00:00,000 --> 00:00:01,000\nEins\n\n2\n00:00:03,000 --> 00:00:04,000\nZwei\n");
    }

    [Fact]
    public void Segments_AreOrderedByIndex()
    {
        TranscriptSegment[] segments = [Segment(1, 1_000, 2_000, "Zwei"), Segment(0, 0, 1_000, "Eins")];

        SubtitleFormatter.ToSrt(segments).Should().StartWith("1\n00:00:00,000 --> 00:00:01,000\nEins");
    }

    [Fact]
    public void ToVtt_EscapesMarkupCharacters()
    {
        var vtt = SubtitleFormatter.ToVtt([Segment(0, 0, 1_000, "A & B <3 --> C")]);

        vtt.Should().Contain("A &amp; B &lt;3 --&gt; C");
    }

    [Fact]
    public void ToSrt_KeepsSpecialCharacters() =>
        SubtitleFormatter.ToSrt([Segment(0, 0, 1_000, "Grüße & „Zitate“ – 100 %")])
            .Should().Contain("Grüße & „Zitate“ – 100 %");

    [Fact]
    public void Text_IsNormalizedToSingleSpacesAndWrappedAt42Characters()
    {
        var srt = SubtitleFormatter.ToSrt([Segment(0, 0, 1_000,
            "  Dies ist ein\nsehr langer Satz, der über mehrere Zeilen umgebrochen werden muss, weil er so lang ist. ")]);

        var lines = srt.Split('\n')[2..^1];
        lines.Should().Equal(
            "Dies ist ein sehr langer Satz, der über",
            "mehrere Zeilen umgebrochen werden muss,",
            "weil er so lang ist.");
        lines.Should().AllSatisfy(l => l.Length.Should().BeLessThanOrEqualTo(42));
    }

    [Fact]
    public void Text_WithWordLongerThanLine_KeepsTheWordWhole()
    {
        var word = new string('x', 50);

        var srt = SubtitleFormatter.ToSrt([Segment(0, 0, 1_000, $"kurz {word} ende")]);

        srt.Should().Contain($"kurz\n{word}\nende");
    }

    [Fact]
    public void Text_OfExactlyOneLineLength_IsNotWrapped()
    {
        const string text = "Das ist genau zweiundvierzig Zeichen lang.";
        text.Length.Should().Be(SubtitleFormatter.MaxLineLength);

        SubtitleFormatter.ToSrt([Segment(0, 0, 1_000, text + " Ja")]).Should().Contain($"\n{text}\nJa\n");
    }

    [Fact]
    public void Text_StartingWithOverlongWord_HasNoEmptyFirstLine()
    {
        var word = new string('y', 50);

        SubtitleFormatter.ToSrt([Segment(0, 0, 1_000, $"{word} ende")]).Should().Contain($"--> 00:00:01,000\n{word}\nende\n");
    }

    [Fact]
    public void NegativeOrReversedTimes_AreClamped() =>
        SubtitleFormatter.ToSrt([Segment(0, -5, -10, "x")]).Should().Contain("00:00:00,000 --> 00:00:00,000");

    [Fact]
    public void NoSegments_ProduceEmptySrtAndHeaderOnlyVtt()
    {
        SubtitleFormatter.ToSrt([]).Should().BeEmpty();
        SubtitleFormatter.ToVtt([]).Should().Be("WEBVTT\n");
    }

    // --- speaker names (S11-T4) ------------------------------------------------

    private static readonly IReadOnlyDictionary<int, string> Speakers = new Dictionary<int, string> { [0] = "Anna", [1] = "Ben" };

    [Fact]
    public void ToSrt_WithSpeakerNames_PrefixesEachCueWithItsSpeaker()
    {
        TranscriptSegment[] segments =
        [
            Segment(0, 0, 1_000, "Hallo.", speakerIndex: 0),
            Segment(1, 1_000, 2_000, "Hi.", speakerIndex: 1),
        ];

        var srt = SubtitleFormatter.ToSrt(segments, Speakers);

        srt.Should().Contain("Anna: Hallo.").And.Contain("Ben: Hi.");
    }

    [Fact]
    public void ToVtt_WithSpeakerNames_WrapsEachCueInAVoiceSpan()
    {
        TranscriptSegment[] segments = [Segment(0, 0, 1_000, "Hallo.", speakerIndex: 0)];

        SubtitleFormatter.ToVtt(segments, Speakers).Should().Contain("<v Anna>Hallo.</v>");
    }

    [Fact]
    public void WithoutSpeakerNamesArgument_NoPrefixIsAdded()
    {
        TranscriptSegment[] segments = [Segment(0, 0, 1_000, "Hallo.", speakerIndex: 0)];

        SubtitleFormatter.ToSrt(segments).Should().Contain("Hallo.").And.NotContain("Anna");
    }

    [Fact]
    public void SegmentWithoutASpeaker_GetsNoPrefixEvenWhenNamesAreGiven()
    {
        TranscriptSegment[] segments = [Segment(0, 0, 1_000, "Hallo.")]; // SpeakerIndex left null

        var srt = SubtitleFormatter.ToSrt(segments, Speakers);
        srt.Should().Contain("Hallo.");
        srt.Should().NotContain("Anna:").And.NotContain("Ben:");
    }

    [Fact]
    public void SpeakerIndexUnknownToTheMap_GetsNoPrefix()
    {
        TranscriptSegment[] segments = [Segment(0, 0, 1_000, "Hallo.", speakerIndex: 99)];

        var srt = SubtitleFormatter.ToSrt(segments, Speakers);
        srt.Should().Contain("Hallo.");
        srt.Should().NotContain("Anna:").And.NotContain("Ben:");
    }

    [Fact]
    public void ToVtt_EscapesTheSpeakerNameToo()
    {
        TranscriptSegment[] segments = [Segment(0, 0, 1_000, "Hi.", speakerIndex: 0)];
        var speakers = new Dictionary<int, string> { [0] = "A & B" };

        SubtitleFormatter.ToVtt(segments, speakers).Should().Contain("<v A &amp; B>Hi.</v>");
    }
}
