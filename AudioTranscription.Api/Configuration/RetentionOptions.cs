namespace AudioTranscription.Api.Configuration;

/// <summary>
/// Automatic deletion of old data (S30). Everything is off by default (0): nothing is deleted unless an
/// administrator configures a period. Only finished jobs expire, and never a job its owner has locked.
/// </summary>
public class RetentionOptions
{
    public const string SectionName = "Retention";

    /// <summary>
    /// Days after completion (or creation, for jobs that never completed) after which a whole job is deleted:
    /// transcript, segments, variants, speakers, shares and upload. 0 keeps jobs forever.
    /// </summary>
    public int TranscriptDays { get; set; }

    /// <summary>
    /// Days after which only the uploaded audio file is deleted while the transcript stays. 0 keeps audio files.
    /// Has no effect when uploads are deleted right after transcription (<c>Upload:DeleteAfterTranscription</c>).
    /// </summary>
    public int AudioDays { get; set; }

    /// <summary>How often the retention run repeats, in hours (at least 1).</summary>
    public int RunIntervalHours { get; set; } = 6;
}
