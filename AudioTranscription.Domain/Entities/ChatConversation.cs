namespace AudioTranscription.Domain.Entities;

/// <summary>A private conversation of one user with their transcripts (S24); nobody else can see or continue it.</summary>
public class ChatConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = string.Empty;
    /// <summary>The beginning of the first question.</summary>
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>One message of a conversation; assistant messages carry their citations as JSON (job, start, end, source number).</summary>
public class ConversationMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    /// <summary>"user" or "assistant".</summary>
    public string Role { get; set; } = "user";
    public string Text { get; set; } = string.Empty;
    public string? CitationsJson { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
