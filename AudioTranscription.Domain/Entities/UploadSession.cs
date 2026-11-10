namespace AudioTranscription.Domain.Entities;

/// <summary>
/// A resumable (tus) upload in progress or just finished (S27, ADR 0006). It binds the upload to the user who
/// started it and remembers the outcome, so the client can ask for the job once the last chunk arrived.
/// </summary>
public class UploadSession
{
    /// <summary>The tus file id, also the last segment of the upload URL.</summary>
    public string Id { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    /// <summary>The job created from the assembled file; null while incomplete or when the file was rejected.</summary>
    public Guid? JobId { get; set; }
    /// <summary>Why the assembled file was rejected (HTTP status and message); null otherwise.</summary>
    public int? RejectionStatus { get; set; }
    public string? RejectionDetail { get; set; }

    /// <summary>True once the last chunk was processed, whether it led to a job or a rejection.</summary>
    public bool IsClosed => JobId is not null || RejectionStatus is not null;
}
