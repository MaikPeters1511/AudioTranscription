namespace AudioTranscription.Domain.Entities;

/// <summary>
/// With which embedding model the vector index (S23) was built; there is at most one row. Vectors of different models
/// cannot be compared, so a configuration that differs from this row makes the index stale until it is rebuilt (ADR 0010).
/// </summary>
public class EmbeddingIndexState
{
    /// <summary>Always 1: the table holds a single row.</summary>
    public int Id { get; set; } = 1;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Dimensions { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
