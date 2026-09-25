using Microsoft.Extensions.Options;

namespace AudioTranscription.Infrastructure.Services;

/// <summary>Rejects an invalid "Diarization" section at startup (registered with ValidateOnStart).</summary>
public class DiarizationOptionsValidator : IValidateOptions<DiarizationOptions>
{
    public ValidateOptionsResult Validate(string? name, DiarizationOptions options)
    {
        var errors = new List<string>();

        if (options.Enabled)
        {
            if (string.IsNullOrWhiteSpace(options.SegmentationModelPath))
                errors.Add("Diarization:SegmentationModelPath must be set while Diarization:Enabled is true.");
            if (string.IsNullOrWhiteSpace(options.EmbeddingModelPath))
                errors.Add("Diarization:EmbeddingModelPath must be set while Diarization:Enabled is true.");
        }

        if (options.Threshold is <= 0f or > 1f)
            errors.Add("Diarization:Threshold must be between 0 (exclusive) and 1 (inclusive).");

        if (options.NumThreads < 1)
            errors.Add("Diarization:NumThreads must be at least 1.");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
