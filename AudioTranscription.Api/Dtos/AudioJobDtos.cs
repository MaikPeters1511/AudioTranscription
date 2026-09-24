using AudioTranscription.Domain.Enums;

namespace AudioTranscription.Api.Dtos;

public record AudioJobDto(
    Guid Id,
    string FileName,
    long FileSizeBytes,
    string ContentType,
    AudioJobStatus Status,
    string? RawTranscript,
    string? ProcessedTranscript,
    string? ErrorMessage,
    string? Language,
    double? DurationSeconds,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    string Model,
    string? RequestedLanguage
);

public record AudioJobListDto(
    Guid Id,
    string FileName,
    long FileSizeBytes,
    AudioJobStatus Status,
    string? Language,
    double? DurationSeconds,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc
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
    IReadOnlyList<string> Languages
);
