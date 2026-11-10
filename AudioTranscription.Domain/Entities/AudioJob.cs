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
    /// <summary>Whisper initial prompt of the glossary chosen at upload (S22); kept when the glossary changes or is deleted.</summary>
    public string? InitialPrompt { get; set; }
    /// <summary>Queue priority (S34): higher runs first, equal priorities run in the order of <see cref="QueuedAtUtc"/>.</summary>
    public int Priority { get; set; }
    /// <summary>When the job was put into the queue (upload or retry); null for jobs that predate S34.</summary>
    public DateTime? QueuedAtUtc { get; set; }
    /// <summary>When the worker started processing; with the file size it yields the processing speed used for estimates.</summary>
    public DateTime? StartedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
}
