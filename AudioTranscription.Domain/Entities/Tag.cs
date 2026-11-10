namespace AudioTranscription.Domain.Entities;

/// <summary>
/// A label a user puts on jobs to find them again (S20). Tags are personal: they belong to one user and are
/// invisible to everyone else, also on a job that is shared between users.
/// </summary>
public class Tag
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Id of the user (ASP.NET Core Identity) the tag belongs to.</summary>
    public string OwnerId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Display color as <c>#RRGGBB</c>, or null for the default look.</summary>
    public string? Color { get; set; }
}

/// <summary>A tag put on a job. The tag's owner decides; it does not show for other users of the job.</summary>
public class JobTag
{
    public Guid AudioJobId { get; set; }
    public Guid TagId { get; set; }
}
