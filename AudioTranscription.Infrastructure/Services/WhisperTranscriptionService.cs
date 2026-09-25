using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;

namespace AudioTranscription.Infrastructure.Services;

/// <summary>
/// Transcription service using Whisper.net (whisper.cpp bindings).
/// Downloads each GGML model on first use, keeps one loaded model per model type
/// (registered as singleton) and converts audio to 16kHz WAV.
/// </summary>
public class WhisperTranscriptionService : ITranscriptionService, IAsyncDisposable
{
    private readonly ILogger<WhisperTranscriptionService> _logger;
    private readonly WhisperOptions _options;
    private readonly Audio16kHzWavConverter _wavConverter;
    private readonly string _modelsDirectory;
    private readonly KeyedAsyncCache<GgmlType, WhisperFactory> _factories;

    public WhisperTranscriptionService(
        IOptions<WhisperOptions> options, Audio16kHzWavConverter wavConverter, ILogger<WhisperTranscriptionService> logger)
    {
        _logger = logger;
        _options = options.Value;
        _wavConverter = wavConverter;
        _modelsDirectory = Path.Combine(AppContext.BaseDirectory, _options.ModelsDirectory);
        _factories = new KeyedAsyncCache<GgmlType, WhisperFactory>(LoadFactoryAsync);
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        string audioFilePath, TranscriptionSettings settings, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        // Also guards jobs stored before an operator removed their model from the allowlist
        if (!_options.TryResolveModel(settings.Model, out var model) || !WhisperOptions.TryParseModelType(model, out var modelType))
            throw new InvalidOperationException($"Das Whisper-Modell '{settings.Model}' ist nicht freigegeben.");

        var factory = await _factories.GetAsync(modelType, cancellationToken);
        var wavPath = await _wavConverter.ConvertAsync(audioFilePath, cancellationToken);

        try
        {
            _logger.LogInformation("Starting transcription for {FilePath} with model {Model}, language={Language}",
                audioFilePath, modelType, settings.Language ?? WhisperOptions.AutomaticLanguage);

            var builder = factory.CreateBuilder();
            builder = settings.Language is null
                ? builder.WithLanguageDetection()
                : builder.WithLanguage(settings.Language);
            if (progress is not null)
                builder = builder.WithProgressHandler(WhisperProgress.Handler(progress));
            using var processor = builder.Build();

            var result = new TranscriptionResultBuilder();
            await using var fileStream = File.OpenRead(wavPath);
            await foreach (var segment in processor.ProcessAsync(fileStream, cancellationToken))
                result.Add(segment.Start, segment.End, segment.Text, segment.Language);

            // Whisper does not always report the last percent
            progress?.Report(100);
            var transcription = result.Build(settings.Language);
            _logger.LogInformation(
                "Transcription complete: {CharCount} chars, {SegmentCount} segments, language={Language}, duration={Duration:F1}s",
                transcription.Text.Length, transcription.Segments.Count, transcription.DetectedLanguage, transcription.DurationSeconds);

            return transcription;
        }
        finally
        {
            // Clean up temporary WAV file if we created one
            if (wavPath != audioFilePath && File.Exists(wavPath))
            {
                try
                {
                    File.Delete(wavPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Failed to delete intermediate WAV file {Path}", wavPath);
                }
            }
        }
    }

    private async Task<WhisperFactory> LoadFactoryAsync(GgmlType modelType, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_modelsDirectory);
        var modelFileName = $"ggml-{modelType.ToString().ToLowerInvariant()}.bin";
        var modelPath = Path.Combine(_modelsDirectory, modelFileName);

        if (!File.Exists(modelPath))
        {
            // Download to a temporary file first so an aborted download never leaves a broken model behind
            var downloadPath = modelPath + ".download";
            _logger.LogInformation("Downloading Whisper model '{Model}' to {Path}...", modelType, modelPath);
            await using (var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(modelType, cancellationToken: cancellationToken))
            await using (var fileStream = File.Create(downloadPath))
            {
                await modelStream.CopyToAsync(fileStream, cancellationToken);
            }
            File.Move(downloadPath, modelPath, overwrite: true);
            _logger.LogInformation("Whisper model downloaded successfully ({Size:F1} MB)",
                new FileInfo(modelPath).Length / (1024.0 * 1024.0));
        }
        else
        {
            _logger.LogInformation("Using existing Whisper model at {Path}", modelPath);
        }

        var factory = WhisperFactory.FromPath(modelPath);
        // S15: only known after the native library actually loads (lazy, on first model use)
        _logger.LogInformation("Whisper native runtime in use: {Runtime}", RuntimeOptions.LoadedLibrary);
        return factory;
    }

    public async ValueTask DisposeAsync()
    {
        await _factories.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
