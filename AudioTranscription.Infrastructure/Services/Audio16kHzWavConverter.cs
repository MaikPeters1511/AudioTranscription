using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace AudioTranscription.Infrastructure.Services;

/// <summary>
/// Converts any input audio to 16kHz mono 16-bit PCM WAV via the system ffmpeg binary, the format both
/// Whisper transcription and speaker diarization (S11) need. Shared so the conversion runs once per job.
/// </summary>
public class Audio16kHzWavConverter(ILogger<Audio16kHzWavConverter> logger)
{
    /// <returns>The input path unchanged if it is already a matching WAV, otherwise a new, converted file next to it.</returns>
    public async Task<string> ConvertAsync(string inputPath, CancellationToken cancellationToken)
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

        logger.LogInformation("Converting audio to 16kHz WAV: {Path}", inputPath);

        var outputPath = Path.Combine(
            Path.GetDirectoryName(inputPath)!,
            $"{Path.GetFileNameWithoutExtension(inputPath)}_16khz.wav");

        await RunFfmpegAsync(inputPath, outputPath, cancellationToken);

        logger.LogInformation("Audio converted to 16kHz WAV: {Path}", outputPath);
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
            logger.LogError("ffmpeg conversion failed (exit code {ExitCode}): {StdErr}", process.ExitCode, stderr);
            throw new InvalidOperationException($"Audiokonvertierung fehlgeschlagen (ffmpeg exit code {process.ExitCode}).");
        }
    }
}
