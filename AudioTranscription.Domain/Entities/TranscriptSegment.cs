namespace AudioTranscription.Domain.Entities;

/// <summary>
/// A timed part of a job's raw transcript as returned by Whisper. Post-processing does not change segments.
/// </summary>
public class TranscriptSegment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AudioJobId { get; set; }
    /// <summary>Position within the transcript, starting at 0.</summary>
    public int Index { get; set; }
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public string Text { get; set; } = string.Empty;
    /// <summary>Speaker this segment was assigned to (S11), by largest time overlap; null without diarization.</summary>
    public int? SpeakerIndex { get; set; }
}
