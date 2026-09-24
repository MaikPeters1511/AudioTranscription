namespace AudioTranscription.Api.Configuration;

public class UploadOptions
{
    public const string SectionName = "Upload";

    public long MaxFileSizeBytes { get; set; } = 10_485_760; // 10 MB
    public string[] AllowedContentTypes { get; set; } =
    [
        "audio/mpeg",
        "audio/wav",
        "audio/x-wav",
        "audio/mp4",
        "audio/x-m4a",
        "audio/ogg"
    ];
    public string TempStoragePath { get; set; } = "temp-uploads";
    public bool DeleteAfterTranscription { get; set; } = true;
    public int OrphanedFileRetentionHours { get; set; } = 24;
}
