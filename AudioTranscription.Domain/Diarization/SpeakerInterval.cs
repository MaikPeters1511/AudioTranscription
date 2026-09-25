namespace AudioTranscription.Domain.Diarization;

/// <summary>One detected speaker's time range from diarization, in the job's audio timeline (S11).</summary>
public record SpeakerInterval(long StartMs, long EndMs, int SpeakerIndex);
