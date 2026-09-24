namespace AudioTranscription.Infrastructure.Services;

/// <summary>
/// Result of a transcription operation.
/// </summary>
public record TranscriptionResult(
    string Text,
    string? DetectedLanguage,
    double? DurationSeconds
)
{
    /// <summary>Timed segments of <see cref="Text"/>, in order.</summary>
    public IReadOnlyList<SegmentResult> Segments { get; init; } = [];
}

/// <summary>A timed part of the transcript as reported by Whisper.</summary>
public record SegmentResult(TimeSpan Start, TimeSpan End, string Text);

/// <summary>
/// Per-job transcription settings.
/// </summary>
/// <param name="Model">Whisper model name as listed in <see cref="WhisperOptions.AllowedModels"/>.</param>
/// <param name="Language">ISO-639-1 code, or null to detect the language.</param>
public record TranscriptionSettings(string Model, string? Language);

/// <summary>
/// Abstraction for the transcription engine.
/// </summary>
public interface ITranscriptionService
{
    /// <summary>
    /// Transcribes an audio file to text.
    /// </summary>
    /// <param name="audioFilePath">Path to the audio file.</param>
    /// <param name="settings">Model and language to use.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Transcription result with text, detected language, and duration.</returns>
    Task<TranscriptionResult> TranscribeAsync(string audioFilePath, TranscriptionSettings settings, CancellationToken cancellationToken = default);
}
