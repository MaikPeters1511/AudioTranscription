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

    /// <summary>How often the retention run repeats, in hours (1 to one week).</summary>
    public int RunIntervalHours { get; set; } = 6;

    /// <summary>
    /// Upper limit of jobs deleted per run, oldest first; the rest follows at the next run. Protects against
    /// wiping everything at once after the period was switched on or the clock jumped forward.
    /// </summary>
    public int MaxJobsPerRun { get; set; } = 500;
}

/// <summary>Stops the start with a clear message for settings that would misbehave later (S30).</summary>
public class RetentionOptionsValidator : Microsoft.Extensions.Options.IValidateOptions<RetentionOptions>
{
    /// <summary>One hundred years: far beyond any real period, far below DateTime overflow.</summary>
    public const int MaxDays = 36_500;
    public const int MaxIntervalHours = 168;
    public const int MaxJobsPerRunLimit = 100_000;

    public Microsoft.Extensions.Options.ValidateOptionsResult Validate(string? name, RetentionOptions options)
    {
        var errors = new List<string>();
        if (options.TranscriptDays is < 0 or > MaxDays)
            errors.Add($"Retention:{nameof(RetentionOptions.TranscriptDays)} must be between 0 (off) and {MaxDays}.");
        if (options.AudioDays is < 0 or > MaxDays)
            errors.Add($"Retention:{nameof(RetentionOptions.AudioDays)} must be between 0 (off) and {MaxDays}.");
        if (options.RunIntervalHours is < 1 or > MaxIntervalHours)
            errors.Add($"Retention:{nameof(RetentionOptions.RunIntervalHours)} must be between 1 and {MaxIntervalHours}.");
        if (options.MaxJobsPerRun is < 1 or > MaxJobsPerRunLimit)
            errors.Add($"Retention:{nameof(RetentionOptions.MaxJobsPerRun)} must be between 1 and {MaxJobsPerRunLimit}.");

        return errors.Count == 0
            ? Microsoft.Extensions.Options.ValidateOptionsResult.Success
            : Microsoft.Extensions.Options.ValidateOptionsResult.Fail(errors);
    }
}
