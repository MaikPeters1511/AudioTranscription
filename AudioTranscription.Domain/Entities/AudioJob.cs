using AudioTranscription.Domain.Enums;

namespace AudioTranscription.Domain.Entities;

public class AudioJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public AudioJobStatus Status { get; set; } = AudioJobStatus.Pending;
    /// <summary>Unmodified Whisper output.</summary>
    public string? RawTranscript { get; set; }
    public string? ErrorMessage { get; set; }
    /// <summary>Detected language, or the requested one if detection was off.</summary>
    public string? Language { get; set; }
    /// <summary>Whisper model chosen at upload, e.g. "Base".</summary>
    public string Model { get; set; } = string.Empty;
    /// <summary>Language chosen at upload (ISO-639-1); null means automatic detection.</summary>
    public string? RequestedLanguage { get; set; }
    /// <summary>Whether speaker diarization (S11) was requested at upload.</summary>
    public bool DiarizationRequested { get; set; }
    public double? DurationSeconds { get; set; }
    /// <summary>
    /// Id of the user (ASP.NET Core Identity) who uploaded the job and is the only one who may change or
    /// delete it (S19). Null for jobs that predate user ownership and have not been assigned yet; only an
    /// admin can see and assign those.
    /// </summary>
    public string? OwnerId { get; set; }
    /// <summary>
    /// True when the owner excluded the job from automatic deletion (S30, retention): neither the
    /// transcript nor the upload is removed by the retention service while this is set.
    /// </summary>
    public bool RetentionLocked { get; set; }
    /// <summary>The owner's folder the job is filed in (S20), or null for the top level.</summary>
    public Guid? FolderId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
}
