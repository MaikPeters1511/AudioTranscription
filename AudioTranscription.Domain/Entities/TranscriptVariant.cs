using AudioTranscription.Domain.Enums;

namespace AudioTranscription.Domain.Entities;

/// <summary>
/// An on-demand result generated from a job's raw transcript (S10): a cleanup, a summary, bullet points,
/// action items, or a translation. There is at most one variant per (AudioJobId, Mode, TargetLanguage);
/// generating again overwrites it rather than creating a second row.
/// </summary>
public class TranscriptVariant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AudioJobId { get; set; }
    public PostProcessingMode Mode { get; set; }
    /// <summary>ISO-639-1 code; only set (and required) for <see cref="PostProcessingMode.Translate"/>.</summary>
    public string? TargetLanguage { get; set; }
    public VariantStatus Status { get; set; } = VariantStatus.Pending;
    public string? Text { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
}
