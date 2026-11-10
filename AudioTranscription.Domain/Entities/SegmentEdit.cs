namespace AudioTranscription.Domain.Entities;

/// <summary>One correction of a <see cref="TranscriptSegment"/> (S32). Version 0, the original, lives on the segment itself.</summary>
public class SegmentEdit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SegmentId { get; set; }
    /// <summary>1-based; a restore creates a new version instead of rewinding.</summary>
    public int Version { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? EditedBy { get; set; }
    public DateTime EditedAtUtc { get; set; } = DateTime.UtcNow;
}
