namespace AudioTranscription.Domain.Entities;

/// <summary>
/// One speaker detected by diarization (S11) for a job. <see cref="Index"/> matches
/// <see cref="TranscriptSegment.SpeakerIndex"/>; a job has at most one row per detected speaker.
/// </summary>
public class JobSpeaker
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AudioJobId { get; set; }
    public int Index { get; set; }
    /// <summary>User-chosen name (e.g. "Anna"); null until renamed, then the UI shows "Sprecher {Index + 1}".</summary>
    public string? DisplayName { get; set; }
}
