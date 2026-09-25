using AudioTranscription.Domain.Diarization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.Wave;
using SherpaOnnx;

namespace AudioTranscription.Infrastructure.Services;

/// <summary>
/// Speaker diarization via sherpa-onnx's <see cref="OfflineSpeakerDiarization"/> (pyannote segmentation
/// + speaker embeddings + clustering), fully offline. See ADR 0004 for the choice of library; the two
/// ONNX models it needs are not bundled and must be placed locally by the operator (<see cref="DiarizationOptions"/>).
/// </summary>
/// <remarks>
/// Model loading is lazy and cached for the process lifetime, like <see cref="WhisperTranscriptionService"/>'s
/// factory cache. Calls are serialized with a lock: the native object's thread-safety is undocumented,
/// and only one diarization runs at a time in this app anyway (inline in <c>TranscriptionWorker</c>).
/// </remarks>
public sealed class SherpaOnnxDiarizationService(
    IOptions<DiarizationOptions> options,
    Audio16kHzWavConverter wavConverter,
    ILogger<SherpaOnnxDiarizationService> logger) : IDiarizationService, IDisposable
{
    private readonly DiarizationOptions _options = options.Value;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private OfflineSpeakerDiarization? _diarization;

    public async Task<IReadOnlyList<SpeakerInterval>> DiarizeAsync(
        string audioFilePath, int? expectedSpeakerCount, CancellationToken cancellationToken = default)
    {
        var wavPath = await wavConverter.ConvertAsync(audioFilePath, cancellationToken);
        var samples = ReadMonoFloatSamples(wavPath);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var diarization = _diarization ??= CreateDiarization();
            // Only the clustering part is meant to change per call; the models stay loaded (see remarks)
            diarization.SetConfig(BuildConfig(expectedSpeakerCount));

            logger.LogInformation("Running speaker diarization on {Path}", audioFilePath);
            var segments = diarization.Process(samples);
            logger.LogInformation("Diarization found {SpeakerCount} speaker(s) in {SegmentCount} segment(s)",
                segments.Select(s => s.Speaker).Distinct().Count(), segments.Length);

            return segments
                .Select(s => new SpeakerInterval(ToMs(s.Start), ToMs(s.End), s.Speaker))
                .OrderBy(interval => interval.StartMs)
                .ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    private OfflineSpeakerDiarization CreateDiarization()
    {
        logger.LogInformation("Loading diarization models (segmentation={Segmentation}, embedding={Embedding})",
            _options.SegmentationModelPath, _options.EmbeddingModelPath);
        return new OfflineSpeakerDiarization(BuildConfig(expectedSpeakerCount: null));
    }

    private OfflineSpeakerDiarizationConfig BuildConfig(int? expectedSpeakerCount)
    {
        var config = new OfflineSpeakerDiarizationConfig
        {
            MinDurationOn = 0.3f,
            MinDurationOff = 0.5f,
        };
        config.Segmentation.Pyannote.Model = _options.SegmentationModelPath;
        config.Segmentation.NumThreads = _options.NumThreads;
        config.Embedding.Model = _options.EmbeddingModelPath;
        config.Embedding.NumThreads = _options.NumThreads;
        // A known speaker count skips clustering by threshold entirely (sherpa-onnx convention: -1 = automatic)
        config.Clustering.NumClusters = expectedSpeakerCount ?? -1;
        config.Clustering.Threshold = _options.Threshold;
        return config;
    }

    private static long ToMs(float seconds) => (long)Math.Round(seconds * 1000);

    private static float[] ReadMonoFloatSamples(string wavPath)
    {
        using var reader = new WaveFileReader(wavPath);
        var provider = reader.ToSampleProvider();
        var buffer = new float[reader.Length / (reader.WaveFormat.BitsPerSample / 8)];
        var read = provider.Read(buffer.AsSpan());
        return read == buffer.Length ? buffer : buffer[..read];
    }

    public void Dispose()
    {
        _diarization?.Dispose();
        _lock.Dispose();
    }
}
