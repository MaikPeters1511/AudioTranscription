namespace AudioTranscription.Infrastructure.Services;

/// <summary>
/// Result of a transcription operation.
/// </summary>
public record TranscriptionResult(
    string Text,
    string? DetectedLanguage,
    double? DurationSeconds
);

/// <summary>
/// Abstraction for the transcription engine.
/// </summary>
public interface ITranscriptionService
{
    /// <summary>
    /// Transcribes an audio file to text.
    /// </summary>
    /// <param name="audioFilePath">Path to the audio file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Transcription result with text, detected language, and duration.</returns>
    Task<TranscriptionResult> TranscribeAsync(string audioFilePath, CancellationToken cancellationToken = default);
}
