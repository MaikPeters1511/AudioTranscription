using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.Wave;
using Whisper.net;
using Whisper.net.Ggml;

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
    private readonly string _modelsDirectory;
    private readonly KeyedAsyncCache<GgmlType, WhisperFactory> _factories;

    public WhisperTranscriptionService(IOptions<WhisperOptions> options, ILogger<WhisperTranscriptionService> logger)
    {
        _logger = logger;
        _options = options.Value;
        _modelsDirectory = Path.Combine(AppContext.BaseDirectory, _options.ModelsDirectory);
        _factories = new KeyedAsyncCache<GgmlType, WhisperFactory>(LoadFactoryAsync);
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        string audioFilePath, TranscriptionSettings settings, CancellationToken cancellationToken = default)
    {
        // Also guards jobs stored before an operator removed their model from the allowlist
        if (!_options.TryResolveModel(settings.Model, out var model) || !WhisperOptions.TryParseModelType(model, out var modelType))
            throw new InvalidOperationException($"Das Whisper-Modell '{settings.Model}' ist nicht freigegeben.");

        var factory = await _factories.GetAsync(modelType, cancellationToken);
        var wavPath = await ConvertToWavAsync(audioFilePath, cancellationToken);

        try
        {
            _logger.LogInformation("Starting transcription for {FilePath} with model {Model}, language={Language}",
                audioFilePath, modelType, settings.Language ?? WhisperOptions.AutomaticLanguage);

            var builder = factory.CreateBuilder();
            builder = settings.Language is null
                ? builder.WithLanguageDetection()
                : builder.WithLanguage(settings.Language);
            using var processor = builder.Build();

            var result = new TranscriptionResultBuilder();
            await using var fileStream = File.OpenRead(wavPath);
            await foreach (var segment in processor.ProcessAsync(fileStream, cancellationToken))
                result.Add(segment.Start, segment.End, segment.Text, segment.Language);

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

        return WhisperFactory.FromPath(modelPath);
    }

    /// <summary>
    /// Converts any supported audio format to 16kHz mono 16-bit PCM WAV as required by Whisper.
    /// Delegates to the system ffmpeg binary, which handles every input codec/container
    /// uniformly across Windows, Linux and macOS (NAudio's MediaFoundation classes are
    /// Windows-only and cannot be used here).
    /// </summary>
    private async Task<string> ConvertToWavAsync(string inputPath, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(inputPath).ToLowerInvariant();

        // If already a WAV, check if it needs resampling
        if (extension == ".wav")
        {
            using var reader = new WaveFileReader(inputPath);
            if (reader.WaveFormat.SampleRate == 16000 &&
                reader.WaveFormat.Channels == 1 &&
                reader.WaveFormat.BitsPerSample == 16)
            {
                return inputPath; // Already in correct format
            }
        }

        _logger.LogInformation("Converting audio to 16kHz WAV: {Path}", inputPath);

        var outputPath = Path.Combine(
            Path.GetDirectoryName(inputPath)!,
            $"{Path.GetFileNameWithoutExtension(inputPath)}_16khz.wav");

        await RunFfmpegAsync(inputPath, outputPath, cancellationToken);

        _logger.LogInformation("Audio converted to 16kHz WAV: {Path}", outputPath);
        return outputPath;
    }

    private async Task RunFfmpegAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            ArgumentList =
            {
                "-y",                 // overwrite output without prompting
                "-i", inputPath,
                "-ac", "1",           // mono
                "-ar", "16000",       // 16 kHz
                "-sample_fmt", "s16",
                "-f", "wav",
                outputPath,
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException(
                "ffmpeg wurde nicht gefunden. Bitte installiere ffmpeg und stelle sicher, dass es im PATH verfügbar ist " +
                "(z.B. 'brew install ffmpeg' auf macOS oder 'apt install ffmpeg' auf Linux).", ex);
        }

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        var stderr = await stderrTask;
        await stdoutTask;

        if (process.ExitCode != 0)
        {
            _logger.LogError("ffmpeg conversion failed (exit code {ExitCode}): {StdErr}", process.ExitCode, stderr);
            throw new InvalidOperationException($"Audiokonvertierung fehlgeschlagen (ffmpeg exit code {process.ExitCode}).");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _factories.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
