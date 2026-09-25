using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace AudioTranscription.Infrastructure.Services;

/// <summary>
/// Rejects an invalid "Whisper" section at startup (registered with ValidateOnStart),
/// instead of failing on the first upload.
/// </summary>
public partial class WhisperOptionsValidator : IValidateOptions<WhisperOptions>
{
    [GeneratedRegex("^[a-z]{2,3}$")]
    private static partial Regex LanguageCode();

    public ValidateOptionsResult Validate(string? name, WhisperOptions options)
    {
        var errors = new List<string>();

        foreach (var model in options.AllowedModels.Where(m => !WhisperOptions.TryParseModelType(m, out _)))
            errors.Add($"Whisper:AllowedModels contains '{model}', which is no Whisper model.");

        if (!options.AllowedModels.Contains(options.DefaultModel, StringComparer.OrdinalIgnoreCase))
            errors.Add($"Whisper:DefaultModel '{options.DefaultModel}' must be one of Whisper:AllowedModels.");

        foreach (var language in options.SupportedLanguages.Where(l => !LanguageCode().IsMatch(l)))
            errors.Add($"Whisper:SupportedLanguages contains '{language}'; use lower-case ISO-639-1 codes (\"auto\" is always available).");

        if (string.IsNullOrWhiteSpace(options.ModelsDirectory))
            errors.Add("Whisper:ModelsDirectory must not be empty.");

        foreach (var runtime in options.RuntimeOrder.Where(r => !WhisperOptions.TryParseRuntimeLibrary(r, out _)))
            errors.Add($"Whisper:RuntimeOrder contains '{runtime}', which is not a Whisper.net runtime library (Cpu, Cuda, Cuda12, Vulkan, CoreML, OpenVino, CpuNoAvx).");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
