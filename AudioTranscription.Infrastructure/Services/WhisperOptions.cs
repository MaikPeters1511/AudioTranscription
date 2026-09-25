using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;

namespace AudioTranscription.Infrastructure.Services;

/// <summary>
/// Configuration section "Whisper": which models and languages users may choose per upload.
/// The lists have no defaults here because the configuration binder appends to pre-filled arrays;
/// they are set in appsettings.json.
/// </summary>
public class WhisperOptions
{
    public const string SectionName = "Whisper";

    /// <summary>Value for requests without a language: Whisper detects it.</summary>
    public const string AutomaticLanguage = "auto";

    /// <summary>Model used when an upload names none; must be one of <see cref="AllowedModels"/>.</summary>
    public string DefaultModel { get; set; } = "Base";

    /// <summary>Selectable models, named like <see cref="GgmlType"/> (e.g. "Base", "LargeV3").</summary>
    public string[] AllowedModels { get; set; } = [];

    /// <summary>Selectable languages as lower-case ISO-639-1 codes; "auto" is always available.</summary>
    public string[] SupportedLanguages { get; set; } = [];

    /// <summary>Where downloaded models are kept; relative paths are resolved against the application directory.</summary>
    public string ModelsDirectory { get; set; } = "whisper-models";

    /// <summary>
    /// Preferred native runtime order (S15), named like <see cref="RuntimeLibrary"/> (e.g. "Cuda", "CoreML", "Cpu").
    /// Empty keeps Whisper.net's own default order, which already ends in "Cpu" as a fallback.
    /// </summary>
    public string[] RuntimeOrder { get; set; } = [];

    /// <summary>
    /// Maps a requested model (case-insensitive, null or empty for the default) to its configured name.
    /// </summary>
    /// <returns>false if the model is not allowed.</returns>
    public bool TryResolveModel(string? requested, out string model)
    {
        var name = string.IsNullOrWhiteSpace(requested) ? DefaultModel : requested.Trim();
        model = AllowedModels.FirstOrDefault(m => m.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        return model.Length > 0;
    }

    /// <summary>
    /// Maps a requested language (case-insensitive) to its ISO code, or to null for automatic detection
    /// (null, empty or "auto").
    /// </summary>
    /// <returns>false if the language is not supported.</returns>
    public bool TryResolveLanguage(string? requested, out string? language)
    {
        language = null;
        if (string.IsNullOrWhiteSpace(requested) ||
            requested.Trim().Equals(AutomaticLanguage, StringComparison.OrdinalIgnoreCase))
            return true;

        language = SupportedLanguages.FirstOrDefault(l => l.Equals(requested.Trim(), StringComparison.OrdinalIgnoreCase));
        return language is not null;
    }

    /// <summary>The Whisper.net model type for a configured model name.</summary>
    public static bool TryParseModelType(string? model, out GgmlType type)
    {
        type = default;
        // Only plain names: Enum.TryParse would also accept numbers and comma-separated combinations
        return !string.IsNullOrEmpty(model) && char.IsAsciiLetter(model[0]) && model.All(char.IsAsciiLetterOrDigit) &&
               Enum.TryParse(model, ignoreCase: true, out type) && Enum.IsDefined(type);
    }

    /// <summary>The Whisper.net native runtime for a configured <see cref="RuntimeOrder"/> entry.</summary>
    public static bool TryParseRuntimeLibrary(string? name, out RuntimeLibrary library)
    {
        library = default;
        return !string.IsNullOrEmpty(name) && char.IsAsciiLetter(name[0]) && name.All(char.IsAsciiLetterOrDigit) &&
               Enum.TryParse(name, ignoreCase: true, out library) && Enum.IsDefined(library);
    }
}
