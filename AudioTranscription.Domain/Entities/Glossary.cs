namespace AudioTranscription.Domain.Entities;

/// <summary>A user's list of names and technical terms that is handed to Whisper as initial prompt (S22).</summary>
public class Glossary
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<GlossaryTerm> Terms { get; set; } = [];
}

public class GlossaryTerm
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GlossaryId { get; set; }
    /// <summary>Position within the glossary; the prompt keeps the order the user chose.</summary>
    public int Position { get; set; }
    public string Term { get; set; } = string.Empty;
}
