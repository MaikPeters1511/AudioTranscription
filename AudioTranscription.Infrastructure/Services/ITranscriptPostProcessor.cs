namespace AudioTranscription.Infrastructure.Services;

/// <summary>
/// Interface for post-processing transcription text (e.g. summarizing, formatting, cleaning up).
/// </summary>
public interface ITranscriptPostProcessor
{
    Task<string> ProcessAsync(string rawTranscript, CancellationToken cancellationToken = default);
}
