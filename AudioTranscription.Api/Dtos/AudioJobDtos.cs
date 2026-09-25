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
    int? ProgressPercent = null
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
    int? ProgressPercent = null
);

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
    string? SpeakerName = null
);

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
