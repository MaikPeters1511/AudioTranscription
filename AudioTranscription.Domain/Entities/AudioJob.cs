using AudioTranscription.Domain.Enums;

namespace AudioTranscription.Domain.Entities;

public class AudioJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public AudioJobStatus Status { get; set; } = AudioJobStatus.Pending;
    /// <summary>Unmodified Whisper output.</summary>
    public string? RawTranscript { get; set; }
    /// <summary>LLM post-processed transcript; null if no post-processing ran or it changed nothing.</summary>
    public string? ProcessedTranscript { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Language { get; set; }
    public double? DurationSeconds { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
}
