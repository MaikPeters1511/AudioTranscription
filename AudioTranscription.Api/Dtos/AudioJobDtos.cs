using AudioTranscription.Domain.Enums;

namespace AudioTranscription.Api.Dtos;

public record TranscriptVariantDto(
    Guid Id,
    PostProcessingMode Mode,
    string? TargetLanguage,
    VariantStatus Status,
    string? Text,
    string? ErrorMessage,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc
);

/// <summary>Body of POST /api/audio-jobs/{id}/variants.</summary>
public record CreateVariantRequest(PostProcessingMode Mode, string? TargetLanguage);

/// <summary>SignalR event "VariantCompleted".</summary>
public record VariantCompletedDto(Guid JobId, Guid VariantId, PostProcessingMode Mode, VariantStatus Status);

public record AudioJobDto(
    Guid Id,
    string FileName,
    long FileSizeBytes,
    string ContentType,
    AudioJobStatus Status,
    string? RawTranscript,
    string? ErrorMessage,
    string? Language,
    double? DurationSeconds,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    string Model,
    string? RequestedLanguage,
    bool DiarizationRequested,
    int? ProgressPercent = null,
    bool? IsOwner = null,
    string? SharedByEmail = null,
    // S30: excluded from automatic deletion, and when the job will otherwise be deleted (null: never or not applicable)
    bool RetentionLocked = false,
    DateTime? DeleteAtUtc = null,
    // S20: the requesting user's own tags, favorite mark and (for the owner) folder; null in live events
    IReadOnlyList<TagDto>? Tags = null,
    bool? IsFavorite = null,
    Guid? FolderId = null
);

public record AudioJobListDto(
    Guid Id,
    string FileName,
    long FileSizeBytes,
    AudioJobStatus Status,
    string? Language,
    double? DurationSeconds,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    int? ProgressPercent = null,
    // Only set when answering a request, not in live events (those go to several users): null means unknown
    bool? IsOwner = null,
    string? SharedByEmail = null,
    // S20: the requesting user's own tags, favorite mark and (for the owner) folder; null in live events
    IReadOnlyList<TagDto>? Tags = null,
    bool? IsFavorite = null,
    Guid? FolderId = null
);

public record TagDto(Guid Id, string Name, string? Color);

public record CreateTagRequest(string? Name, string? Color);

/// <summary>A glossary with its terms and how much of Whisper's prompt window they use (S22).</summary>
public record GlossaryDto(Guid Id, string Name, List<string> Terms, int EstimatedTokens, int MaxTokens);

public record SaveGlossaryRequest(string? Name, List<string>? Terms);

public record SetJobTagsRequest(Guid[]? TagIds);

public record FolderDto(Guid Id, string Name, Guid? ParentId);

public record SaveFolderRequest(string? Name, Guid? ParentId);

public record SetJobFolderRequest(Guid? FolderId);

/// <summary>A user a job is shared with (read only).</summary>
public record JobShareDto(string UserId, string Email);

public record ShareJobRequest(string? Email);

public record SetRetentionLockRequest(bool? Locked);

public record AssignOwnerRequest(string? UserId);

public record CreateAudioJobResponse(Guid Id);

public record PaginatedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize
);

/// <summary>Choices for the upload form; "auto" (language detection) is always available in addition to <see cref="Languages"/>.</summary>
public record TranscriptionOptionsDto(
    IReadOnlyList<string> Models,
    string DefaultModel,
    IReadOnlyList<string> Languages,
    /// <summary>Whether variant generation (S10) is available, i.e. an LLM (Ollama) is configured.</summary>
    bool PostProcessingEnabled,
    /// <summary>Whether speaker diarization (S11) is available, i.e. the sherpa-onnx models are configured.</summary>
    bool DiarizationEnabled
);

public record TranscriptSegmentDto(
    int Index, long StartMs, long EndMs, string Text,
    /// <summary>Set only for a diarized job (S11); null otherwise or when no speaker overlapped this segment.</summary>
    int? SpeakerIndex = null,
    /// <summary>Resolved display name for <see cref="SpeakerIndex"/> ("Sprecher N" until renamed); null when SpeakerIndex is null.</summary>
    string? SpeakerName = null,
    /// <summary>0 = original text; each correction (S32) increments it. Send it back as <c>ExpectedVersion</c> when editing.</summary>
    int Version = 0
);

/// <summary>Correct a segment (S32); <see cref="ExpectedVersion"/> guards against overwriting a concurrent edit (409).</summary>
public record EditSegmentRequest(string Text, int ExpectedVersion);

/// <summary>Restore an earlier version of a segment as a new version (S32).</summary>
public record RestoreSegmentRequest(int Version, int ExpectedVersion);

/// <summary>One entry of a segment's history (S32); version 0 is the original and has no editor.</summary>
public record SegmentVersionDto(int Version, string Text, string? EditedBy, DateTime? EditedAtUtc);

/// <summary>One speaker detected by diarization (S11), with its resolved display name.</summary>
public record JobSpeakerDto(int Index, string DisplayName);

/// <summary>Body of PUT /api/audio-jobs/{id}/speakers/{index}.</summary>
public record RenameSpeakerRequest(string DisplayName);

/// <summary>SignalR event "JobProgress" while a job is being transcribed.</summary>
public record JobProgressDto(Guid JobId, int Percent);

/// <summary>
/// One full-text search hit (S13): a snippet of the matched text, with the match's position inside
/// the snippet as offsets (not HTML, see ADR 0005), so the frontend can render its own &lt;mark&gt;.
/// </summary>
public record SearchHitDto(
    Guid JobId,
    string FileName,
    string Snippet,
    int HighlightStart,
    int HighlightLength,
    /// <summary>Set when the hit came from a timed segment (S06); null for a job- or variant-level hit.</summary>
    long? SegmentStartMs
);

/// <summary>A job in the queue overview (S34): position 0 is the running job, 1 and up are waiting.</summary>
public record QueueEntryDto(
    Guid JobId,
    string FileName,
    AudioJobStatus Status,
    int Position,
    int Priority,
    // Null while there is no finished job to derive the processing speed from
    double? EstimatedWaitSeconds,
    double? EstimatedProcessingSeconds);

/// <summary>The caller's own jobs in the queue; <c>TotalWaiting</c> counts waiting jobs of all users.</summary>
public record QueueDto(IReadOnlyList<QueueEntryDto> Entries, int TotalWaiting);

public record SetPriorityRequest(int? Priority);
