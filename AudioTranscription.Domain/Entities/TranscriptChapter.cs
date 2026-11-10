using AudioTranscription.Domain.Enums;

namespace AudioTranscription.Domain.Entities;

/// <summary>
/// A titled section of a job's recording (S25). Chapters of a job follow each other without gap or overlap and
/// their limits lie on segment boundaries; <see cref="Title"/> and <see cref="Summary"/> can be edited by hand.
/// </summary>
public class TranscriptChapter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AudioJobId { get; set; }
    /// <summary>Order within the job, starting at 0.</summary>
    public int Position { get; set; }
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}

/// <summary>
/// The (re)generation of a job's chapters (S25): at most one per job, which a new request overwrites. Its status
/// works like the one of a transcript variant (S10).
/// </summary>
public class ChapterRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AudioJobId { get; set; }
    public VariantStatus Status { get; set; } = VariantStatus.Pending;
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
}
