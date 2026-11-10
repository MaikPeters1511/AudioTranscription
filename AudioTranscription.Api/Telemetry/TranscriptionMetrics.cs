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

    private readonly Counter<long> _jobs;
    private readonly Histogram<double> _duration;
    private readonly Histogram<double> _realtimeFactor;
    private readonly Histogram<double> _postProcessingDuration;

    public TranscriptionMetrics(IMeterFactory meterFactory, TranscriptionQueue queue)
    {
        var meter = meterFactory.Create(MeterName);
        _jobs = meter.CreateCounter<long>("transcription.jobs", unit: "{job}", description: "Transcription jobs that ended, by outcome");
        _duration = meter.CreateHistogram<double>("transcription.duration", unit: "s", description: "Processing time of a transcription job");
        _realtimeFactor = meter.CreateHistogram<double>("transcription.realtime_factor", description: "Processing time divided by audio length of completed jobs; below 1 is faster than realtime");
        _postProcessingDuration = meter.CreateHistogram<double>("postprocessing.duration", unit: "s", description: "Time to generate a transcript variant with the LLM");
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
