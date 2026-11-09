namespace AudioTranscription.Domain.Entities;

/// <summary>
/// Read-only access to a job that its owner granted to another user (S19). A job has at most one
/// share per user; the share ends with the job or when the owner removes it.
/// </summary>
public class JobShare
{
    public Guid AudioJobId { get; set; }

    /// <summary>Id of the user (ASP.NET Core Identity) who may read the job.</summary>
    public string UserId { get; set; } = string.Empty;

    public DateTime SharedAtUtc { get; set; } = DateTime.UtcNow;
}
