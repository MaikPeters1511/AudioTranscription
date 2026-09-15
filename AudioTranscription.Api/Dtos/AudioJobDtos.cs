using AudioTranscription.Domain.Enums;

namespace AudioTranscription.Api.Dtos;

public record AudioJobDto(
    Guid Id,
    string FileName,
    long FileSizeBytes,
    string ContentType,
    AudioJobStatus Status,
    string? TranscriptText,
    string? ErrorMessage,
    string? Language,
    double? DurationSeconds,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc
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
