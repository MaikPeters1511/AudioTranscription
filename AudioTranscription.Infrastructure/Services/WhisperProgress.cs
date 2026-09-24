using Whisper.net;

namespace AudioTranscription.Infrastructure.Services;

/// <summary>Adapts Whisper.net's progress callback to <see cref="IProgress{T}"/>.</summary>
public static class WhisperProgress
{
    /// <returns>A handler for <c>WithProgressHandler</c> that reports percent values clamped to 0–100.</returns>
    public static OnProgressHandler Handler(IProgress<int> progress) =>
        percent => progress.Report(Math.Clamp(percent, 0, 100));
}
