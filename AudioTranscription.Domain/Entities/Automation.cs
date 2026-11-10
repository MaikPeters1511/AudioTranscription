namespace AudioTranscription.Domain.Entities;

/// <summary>
/// An API key of a user (S28, ADR 0009) for scripts and tools. The key itself is never stored, only its SHA-256
/// hash; the public prefix finds the row. Deleting the row revokes the key.
/// </summary>
public class ApiKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>The 8 public characters of the key (unique), shown in the list so keys can be told apart.</summary>
    public string Prefix { get; set; } = string.Empty;
    public byte[] KeyHash { get; set; } = [];
    /// <summary>Comma-separated scopes, see <c>ApiKeyScopes</c>.</summary>
    public string Scopes { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAtUtc { get; set; }
    /// <summary>Written at most once per minute.</summary>
    public DateTime? LastUsedAtUtc { get; set; }
}

/// <summary>A URL that is called when something happens to a job of its owner (S28, ADR 0009).</summary>
public class Webhook
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    /// <summary>The signing secret, encrypted with Data Protection; never returned by the API after creation.</summary>
    public string ProtectedSecret { get; set; } = string.Empty;
    /// <summary>Comma-separated event names, see <c>WebhookEvents</c>.</summary>
    public string Events { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public enum WebhookDeliveryStatus
{
    /// <summary>Waiting for its first attempt or for the next one.</summary>
    Pending = 0,
    Succeeded = 1,
    /// <summary>Refused by the receiver or out of attempts.</summary>
    Failed = 2,
}

/// <summary>One notification to one webhook: the outbox row and the delivery log in one (ADR 0009).</summary>
public class WebhookDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WebhookId { get; set; }
    public string Event { get; set; } = string.Empty;
    public Guid? JobId { get; set; }
    /// <summary>The JSON body that is sent (and signed) on every attempt.</summary>
    public string Payload { get; set; } = string.Empty;
    public WebhookDeliveryStatus Status { get; set; } = WebhookDeliveryStatus.Pending;
    public int Attempts { get; set; }
    public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;
    public int? LastStatusCode { get; set; }
    /// <summary>A short, fixed error class such as "timeout" or "http 500"; never the receiver's response body.</summary>
    public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
}
