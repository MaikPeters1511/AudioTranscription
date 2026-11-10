namespace AudioTranscription.Domain.Entities;

/// <summary>
/// A folder in a user's own tree to sort the jobs they own (S20). Folders are personal; a job shared with
/// someone else does not appear in the other user's folders.
/// </summary>
public class Folder
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Id of the user (ASP.NET Core Identity) the folder belongs to.</summary>
    public string OwnerId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>The enclosing folder, or null for a folder at the top level.</summary>
    public Guid? ParentId { get; set; }
}
