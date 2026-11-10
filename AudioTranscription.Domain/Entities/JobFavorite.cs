namespace AudioTranscription.Domain.Entities;

/// <summary>A job a user marked as favorite (S20). Personal: each user has their own favorites, also for shared jobs.</summary>
public class JobFavorite
{
    public Guid AudioJobId { get; set; }

    /// <summary>Id of the user (ASP.NET Core Identity) who marked it.</summary>
    public string UserId { get; set; } = string.Empty;
}
