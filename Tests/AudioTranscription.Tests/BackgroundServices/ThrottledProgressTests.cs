using AudioTranscription.Api.BackgroundServices;
using FluentAssertions;

namespace AudioTranscription.Tests.BackgroundServices;

public class ThrottledProgressTests
{
    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }

    private readonly ManualTimeProvider _time = new();
    private readonly List<int> _sent = [];

    private ThrottledProgress Create() => new(_time, _sent.Add);

    [Fact]
    public void ThousandRawValues_ProduceAtMost101IncreasingEventsEndingAt100()
    {
        var progress = Create();

        for (var i = 0; i < 1000; i++)
        {
            progress.Report(i * 100 / 999);
            _time.Advance(TimeSpan.FromMilliseconds(300));
        }

        _sent.Count.Should().BeLessThanOrEqualTo(101);
        _sent.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        _sent[^1].Should().Be(100);
    }

    [Fact]
    public void ValuesWithin250Milliseconds_AreDropped_ButHundredIsAlwaysSent()
    {
        var progress = Create();

        progress.Report(0);
        _time.Advance(TimeSpan.FromMilliseconds(249));
        progress.Report(40);
        progress.Report(99);
        progress.Report(100);

        _sent.Should().Equal(0, 100);
    }

    [Fact]
    public void AValue_IsSentOnceTheIntervalHasPassed()
    {
        var progress = Create();

        progress.Report(5);
        _time.Advance(TimeSpan.FromMilliseconds(250));
        progress.Report(6);

        _sent.Should().Equal(5, 6);
    }

    [Fact]
    public void UnchangedOrDecreasingValues_AreNotSent()
    {
        var progress = Create();

        progress.Report(20);
        _time.Advance(TimeSpan.FromSeconds(1));
        progress.Report(20);
        progress.Report(10);
        progress.Report(100);
        _time.Advance(TimeSpan.FromSeconds(1));
        progress.Report(100);

        _sent.Should().Equal(20, 100);
    }
}
