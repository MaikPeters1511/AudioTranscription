using AudioTranscription.Domain.Diarization;
using FluentAssertions;

namespace AudioTranscription.Tests.Diarization;

public class SpeakerOverlapAssignerTests
{
    [Fact]
    public void Assign_WithNoSpeakerIntervals_ReturnsNull() =>
        SpeakerOverlapAssigner.Assign(0, 1000, []).Should().BeNull();

    [Fact]
    public void Assign_SegmentFullyInsideOneInterval_ReturnsThatSpeaker()
    {
        SpeakerInterval[] intervals = [new(0, 5000, 1)];

        SpeakerOverlapAssigner.Assign(1000, 2000, intervals).Should().Be(1);
    }

    [Fact]
    public void Assign_SegmentSpanningTwoIntervals_ReturnsTheSpeakerWithMoreOverlap()
    {
        // 0-3000ms speaker 0, 3000-10000ms speaker 1; segment 2000-4000ms overlaps 0 by 1000ms, 1 by 1000ms... adjust for a clear winner
        SpeakerInterval[] intervals = [new(0, 2500, 0), new(2500, 10_000, 1)];

        // Segment 1000-4000: overlap with 0 is 1500ms (1000-2500), overlap with 1 is 1500ms (2500-4000) => tie, lower index wins
        SpeakerOverlapAssigner.Assign(1000, 4000, intervals).Should().Be(0);

        // Segment 1000-3000: overlap with 0 is 1500ms, overlap with 1 is 500ms => speaker 0
        SpeakerOverlapAssigner.Assign(1000, 3000, intervals).Should().Be(0);

        // Segment 2000-5000: overlap with 0 is 500ms, overlap with 1 is 2500ms => speaker 1
        SpeakerOverlapAssigner.Assign(2000, 5000, intervals).Should().Be(1);
    }

    [Fact]
    public void Assign_ExactOverlapTie_PicksTheLowerSpeakerIndexRegardlessOfInputOrder()
    {
        SpeakerInterval[] ascending = [new(0, 1000, 2), new(0, 1000, 5)];
        SpeakerInterval[] descending = [new(0, 1000, 5), new(0, 1000, 2)];

        SpeakerOverlapAssigner.Assign(0, 1000, ascending).Should().Be(2);
        SpeakerOverlapAssigner.Assign(0, 1000, descending).Should().Be(2);
    }

    [Fact]
    public void Assign_WithNoOverlappingInterval_ReturnsNull()
    {
        SpeakerInterval[] intervals = [new(0, 1000, 0), new(5000, 6000, 1)];

        // Gap between the two intervals, segment falls entirely in the gap
        SpeakerOverlapAssigner.Assign(2000, 3000, intervals).Should().BeNull();
    }

    [Fact]
    public void Assign_TouchingBoundaryExactly_CountsAsNoOverlap()
    {
        SpeakerInterval[] intervals = [new(0, 1000, 0)];

        SpeakerOverlapAssigner.Assign(1000, 2000, intervals).Should().BeNull();
    }

    [Fact]
    public void Assign_ZeroDurationSegment_ReturnsNull()
    {
        SpeakerInterval[] intervals = [new(0, 5000, 0)];

        SpeakerOverlapAssigner.Assign(2000, 2000, intervals).Should().BeNull();
    }

    [Fact]
    public void Assign_NegativeOrReversedInputIsClampedToNoOverlap()
    {
        SpeakerInterval[] intervals = [new(0, 5000, 0)];

        SpeakerOverlapAssigner.Assign(3000, 1000, intervals).Should().BeNull();
    }
}
