using AudioTranscription.Api.BackgroundServices;
using AudioTranscription.Api.Telemetry;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.Metrics;

namespace AudioTranscription.Tests.Telemetry;

/// <summary>S29-T1: business metrics of the transcription pipeline.</summary>
public class TranscriptionMetricsTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly TranscriptionQueue _queue = new();
    private readonly MetricsCollector _collector;
    private readonly TranscriptionMetrics _metrics;

    public TranscriptionMetricsTests()
    {
        _provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = _provider.GetRequiredService<IMeterFactory>();
        _collector = new MetricsCollector(factory);
        _metrics = new TranscriptionMetrics(factory, _queue);
    }

    public void Dispose()
    {
        _collector.Dispose();
        _provider.Dispose();
    }

    [Fact]
    public void ACompletedJob_IsCountedAndTimed_WithItsRealtimeFactor()
    {
        _metrics.JobFinished(TranscriptionOutcome.Completed, "Base", TimeSpan.FromSeconds(30), audioSeconds: 60);

        var jobs = _collector.Of("transcription.jobs").Should().ContainSingle().Subject;
        jobs.Value.Should().Be(1);
        jobs.Tags.Should().BeEquivalentTo(new Dictionary<string, object?> { ["outcome"] = "completed", ["model"] = "Base" });
        _collector.Of("transcription.duration").Should().ContainSingle().Which.Value.Should().Be(30);
        _collector.Of("transcription.realtime_factor").Should().ContainSingle().Which.Value.Should().Be(0.5);
    }

    [Theory]
    [InlineData(TranscriptionOutcome.Failed)]
    [InlineData(TranscriptionOutcome.Cancelled)]
    public void AFailedOrCancelledJob_IsCountedWithItsOutcome_ButHasNoRealtimeFactor(string outcome)
    {
        _metrics.JobFinished(outcome, "Small", TimeSpan.FromSeconds(5), audioSeconds: null);

        _collector.Of("transcription.jobs").Should().ContainSingle().Which.Tags["outcome"].Should().Be(outcome);
        _collector.Of("transcription.duration").Should().ContainSingle();
        _collector.Of("transcription.realtime_factor").Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void WithoutAUsableAudioLength_NoRealtimeFactorIsReported(double? audioSeconds)
    {
        _metrics.JobFinished(TranscriptionOutcome.Completed, "Base", TimeSpan.FromSeconds(5), audioSeconds);

        _collector.Of("transcription.realtime_factor").Should().BeEmpty("a division by zero must not produce a value");
    }

    [Fact]
    public void TheQueueLength_IsReportedAsAGauge()
    {
        _queue.EnqueueAsync(new TranscriptionJobRequest(Guid.NewGuid(), "a.mp3")).AsTask().GetAwaiter().GetResult();
        _queue.EnqueueAsync(new TranscriptionJobRequest(Guid.NewGuid(), "b.mp3")).AsTask().GetAwaiter().GetResult();
        _queue.EnqueueAsync(new TranscriptionJobRequest(Guid.NewGuid(), "c.mp3")).AsTask().GetAwaiter().GetResult();

        _collector.Observe();

        _collector.Of("transcription.queue.length").Should().ContainSingle().Which.Value.Should().Be(3);
    }

    [Fact]
    public void PostProcessing_IsTimedPerModeAndOutcome()
    {
        _metrics.PostProcessingFinished("Summary", TranscriptionOutcome.Completed, TimeSpan.FromSeconds(12));

        var duration = _collector.Of("postprocessing.duration").Should().ContainSingle().Subject;
        duration.Value.Should().Be(12);
        duration.Tags.Should().BeEquivalentTo(new Dictionary<string, object?> { ["mode"] = "Summary", ["outcome"] = "completed" });
    }

    [Fact]
    public void OnlyAFewBoundedTagsAreUsed_NeverFileNamesOrContent()
    {
        _metrics.JobFinished(TranscriptionOutcome.Completed, "Base", TimeSpan.FromSeconds(1), 2);
        _metrics.JobFinished(TranscriptionOutcome.Failed, "Base", TimeSpan.FromSeconds(1), null);
        _metrics.PostProcessingFinished("Cleanup", TranscriptionOutcome.Failed, TimeSpan.FromSeconds(1));

        _collector.All().SelectMany(m => m.Tags.Keys).Distinct().Should().BeSubsetOf(["outcome", "model", "mode"]);
    }

    [Fact]
    public void TheMeterNameMatchesWhatTheServiceDefaultsRegister()
    {
        // ServiceDefaults cannot reference this project, so it repeats the literal; they must not drift apart
        TranscriptionMetrics.MeterName.Should().Be("AudioTranscription");
        AudioTranscription.Infrastructure.Telemetry.AppTelemetry.Name.Should().Be("AudioTranscription");
    }

    // --- bucket boundaries (S29-F1): with OpenTelemetry's defaults every p50/p95 of a short job lands in the first bucket ---

    private static IReadOnlyList<double> BoundariesOf(string name)
    {
        using var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = provider.GetRequiredService<IMeterFactory>();
        IReadOnlyList<double>? found = null;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, _) =>
            {
                if (instrument.Name == name && instrument is Histogram<double> histogram)
                    found = histogram.Advice?.HistogramBucketBoundaries;
            }
        };
        listener.Start();
        _ = new TranscriptionMetrics(factory, new TranscriptionQueue());
        return found ?? [];
    }

    [Theory]
    [InlineData("transcription.duration")]
    [InlineData("transcription.realtime_factor")]
    [InlineData("postprocessing.duration")]
    public void Histograms_HaveOwnBucketBoundaries_Ascending(string name)
    {
        var boundaries = BoundariesOf(name);

        boundaries.Should().NotBeEmpty($"{name} would otherwise use the default boundaries 0, 5, 10, 25, ...");
        boundaries.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void TheRealtimeFactorBuckets_ResolveTheRangeAroundRealtime()
    {
        var boundaries = BoundariesOf("transcription.realtime_factor");

        boundaries.Should().Contain(1, "one is the border between faster and slower than realtime");
        boundaries.Count(b => b < 1).Should().BeGreaterThanOrEqualTo(3, "fast GPU runs are far below 1");
        boundaries.Count(b => b is > 1 and <= 10).Should().BeGreaterThanOrEqualTo(3, "slow CPU runs are several times realtime");
    }

    [Fact]
    public void TheDurationBuckets_ResolveSecondsAsWellAsHours()
    {
        var boundaries = BoundariesOf("transcription.duration");

        boundaries.First().Should().BeLessThanOrEqualTo(1);
        boundaries.Last().Should().BeGreaterThanOrEqualTo(1800);
        boundaries.Count(b => b <= 30).Should().BeGreaterThanOrEqualTo(4);
    }
}
