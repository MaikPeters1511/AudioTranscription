using System.Diagnostics.Metrics;
using AudioTranscription.Api.BackgroundServices;
using AudioTranscription.Infrastructure.Telemetry;

namespace AudioTranscription.Api.Telemetry;

/// <summary>Values of the <c>outcome</c> tag.</summary>
public static class TranscriptionOutcome
{
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

/// <summary>
/// Business metrics of the transcription pipeline (S29): how many jobs end how, how long they take, how
/// fast we are compared to the audio length (realtime factor = processing time / audio time, below 1 is
/// faster than realtime) and how many jobs wait. Tags are limited to <c>outcome</c>, <c>model</c> and
/// <c>mode</c>, never file names or content.
/// </summary>
public sealed class TranscriptionMetrics
{
    public const string MeterName = AppTelemetry.Name;

    // OpenTelemetry's default boundaries (0, 5, 10, 25, 50, ...) put every short job and every realtime factor around 1 into
    // the first bucket, so p50 and p95 would always read 2.5 and 4.75. These resolve what this service really produces.

    /// <summary>Seconds: a few seconds for a short clip up to an hour and more for long recordings on a CPU.</summary>
    private static readonly double[] DurationBuckets = [0.25, 0.5, 1, 2, 5, 10, 20, 30, 60, 120, 300, 600, 1200, 1800, 3600, 7200];

    /// <summary>Processing time divided by audio length: far below 1 on a GPU, several times realtime on a slow CPU.</summary>
    private static readonly double[] RealtimeFactorBuckets = [0.01, 0.02, 0.05, 0.1, 0.2, 0.35, 0.5, 0.75, 1, 1.5, 2, 3, 5, 10, 20];

    /// <summary>Seconds of an LLM call; a long transcript is split into several calls.</summary>
    private static readonly double[] PostProcessingBuckets = [1, 2, 5, 10, 20, 30, 60, 120, 300, 600];

    private readonly Counter<long> _jobs;
    private readonly Histogram<double> _duration;
    private readonly Histogram<double> _realtimeFactor;
    private readonly Histogram<double> _postProcessingDuration;

    public TranscriptionMetrics(IMeterFactory meterFactory, TranscriptionQueue queue)
    {
        var meter = meterFactory.Create(MeterName);
        _jobs = meter.CreateCounter<long>("transcription.jobs", unit: "{job}", description: "Transcription jobs that ended, by outcome");
        _duration = meter.CreateHistogram<double>("transcription.duration", unit: "s", description: "Processing time of a transcription job",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBuckets });
        _realtimeFactor = meter.CreateHistogram<double>("transcription.realtime_factor", description: "Processing time divided by audio length of completed jobs; below 1 is faster than realtime",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = RealtimeFactorBuckets });
        _postProcessingDuration = meter.CreateHistogram<double>("postprocessing.duration", unit: "s", description: "Time to generate a transcript variant with the LLM",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = PostProcessingBuckets });
        meter.CreateObservableGauge("transcription.queue.length", () => queue.Count, unit: "{job}", description: "Transcription jobs waiting for the worker");
    }

    public void JobFinished(string outcome, string model, TimeSpan processing, double? audioSeconds)
    {
        _jobs.Add(1, new KeyValuePair<string, object?>("outcome", outcome), new KeyValuePair<string, object?>("model", model));
        _duration.Record(processing.TotalSeconds, new KeyValuePair<string, object?>("outcome", outcome), new KeyValuePair<string, object?>("model", model));

        if (outcome == TranscriptionOutcome.Completed && audioSeconds is > 0)
            _realtimeFactor.Record(processing.TotalSeconds / audioSeconds.Value, new KeyValuePair<string, object?>("model", model));
    }

    public void PostProcessingFinished(string mode, string outcome, TimeSpan duration) =>
        _postProcessingDuration.Record(duration.TotalSeconds, new KeyValuePair<string, object?>("mode", mode), new KeyValuePair<string, object?>("outcome", outcome));
}
