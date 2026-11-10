namespace AudioTranscription.Domain.Entities;

/// <summary>
/// A timed part of a job's raw transcript as returned by Whisper. Post-processing does not change segments; user corrections (S32) are stored beside the original.
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
    /// <summary>User correction (S32); null until the segment was edited. <see cref="Text"/> stays the untouched original.</summary>
    public string? CurrentText { get; set; }
    /// <summary>Number of edits so far; 0 is the original. Used as optimistic-concurrency token for edits.</summary>
    public int Version { get; set; }
    /// <summary>The text exports, subtitles, search and the player use: the correction if there is one, else the original.</summary>
    public string EffectiveText => CurrentText ?? Text;
}
