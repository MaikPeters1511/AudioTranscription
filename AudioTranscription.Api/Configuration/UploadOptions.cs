namespace AudioTranscription.Api.Configuration;

public class UploadOptions
{
    public const string SectionName = "Upload";

    /// <summary>
    /// Also drives Kestrel's <c>MaxRequestBodySize</c> and <see cref="Microsoft.AspNetCore.Http.Features.FormOptions"/>'s
    /// <c>MultipartBodyLengthLimit</c> (see <c>Program.cs</c>), so this is the single place to raise the limit (S14).
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 500_000_000; // 500 MB
    public string[] AllowedContentTypes { get; set; } =
    [
        "audio/mpeg",
        "audio/wav",
        "audio/x-wav",
        "audio/mp4",
        "audio/x-m4a",
        "audio/ogg",
        "audio/webm",
        // Video containers (S14): only the audio track is used, see Audio16kHzWavConverter's "-vn" ffmpeg flag
        "video/mp4",
        "video/webm",
        "video/x-matroska",
        "video/quicktime"
    ];
    public string TempStoragePath { get; set; } = "temp-uploads";
    public bool DeleteAfterTranscription { get; set; } = true;
    public int OrphanedFileRetentionHours { get; set; } = 24;
}
