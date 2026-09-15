using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using Whisper.net;
using Whisper.net.Ggml;

namespace AudioTranscription.Infrastructure.Services;

/// <summary>
/// Transcription service using Whisper.net (whisper.cpp bindings).
/// Automatically downloads the GGML model on first use and converts audio to 16kHz WAV.
/// </summary>
public class WhisperTranscriptionService : ITranscriptionService, IAsyncDisposable
{
    private readonly ILogger<WhisperTranscriptionService> _logger;
    private readonly string _modelsDirectory;
    private readonly GgmlType _modelType;
    private WhisperFactory? _factory;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public WhisperTranscriptionService(ILogger<WhisperTranscriptionService> logger)
    {
        _logger = logger;
        _modelsDirectory = Path.Combine(AppContext.BaseDirectory, "whisper-models");
        _modelType = GgmlType.Base;
    }

    public async Task<TranscriptionResult> TranscribeAsync(string audioFilePath, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var wavPath = await ConvertToWavAsync(audioFilePath, cancellationToken);

        try
        {
            _logger.LogInformation("Starting transcription for {FilePath}", audioFilePath);

            using var processor = _factory!.CreateBuilder()
                .WithLanguageDetection()
                .Build();

            var segments = new StringBuilder();
            string? detectedLanguage = null;
            double maxEndTime = 0;

            await using var fileStream = File.OpenRead(wavPath);
            await foreach (var segment in processor.ProcessAsync(fileStream, cancellationToken))
            {
                segments.Append(segment.Text);
                detectedLanguage ??= segment.Language;

                var endSeconds = segment.End.TotalSeconds;
                if (endSeconds > maxEndTime)
                    maxEndTime = endSeconds;
            }

            var text = segments.ToString().Trim();
            _logger.LogInformation(
                "Transcription complete: {CharCount} chars, language={Language}, duration={Duration:F1}s",
                text.Length, detectedLanguage, maxEndTime);

            return new TranscriptionResult(text, detectedLanguage, maxEndTime > 0 ? maxEndTime : null);
        }
        finally
        {
            // Clean up temporary WAV file if we created one
            if (wavPath != audioFilePath && File.Exists(wavPath))
            {
                try { File.Delete(wavPath); }
                catch { /* best effort */ }
            }
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;

            Directory.CreateDirectory(_modelsDirectory);
            var modelFileName = $"ggml-{_modelType.ToString().ToLowerInvariant()}.bin";
            var modelPath = Path.Combine(_modelsDirectory, modelFileName);

            if (!File.Exists(modelPath))
            {
                _logger.LogInformation("Downloading Whisper model '{Model}' to {Path}...", _modelType, modelPath);
                await using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(_modelType, cancellationToken: cancellationToken);
                await using var fileStream = File.Create(modelPath);
                await modelStream.CopyToAsync(fileStream, cancellationToken);
                _logger.LogInformation("Whisper model downloaded successfully ({Size:F1} MB)",
                    new FileInfo(modelPath).Length / (1024.0 * 1024.0));
            }
            else
            {
                _logger.LogInformation("Using existing Whisper model at {Path}", modelPath);
            }

            _factory = WhisperFactory.FromPath(modelPath);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
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

    public ValueTask DisposeAsync()
    {
        _factory?.Dispose();
        _initLock.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
