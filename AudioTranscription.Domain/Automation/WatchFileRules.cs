using System.Security.Cryptography;
using System.Text;

namespace AudioTranscription.Domain.Automation;

/// <summary>Rules for files picked up from a watched folder (S28, ADR 0009).</summary>
public static class WatchFileRules
{
    private const int MaxNameLength = 120;

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav",
        [".m4a"] = "audio/x-m4a",
        [".ogg"] = "audio/ogg",
        [".webm"] = "audio/webm",
        [".mp4"] = "video/mp4",
        [".mkv"] = "video/x-matroska",
        [".mov"] = "video/quicktime",
    };

    /// <summary>The content type for an allowed extension; the extension decides, magic bytes are checked afterwards.</summary>
    public static bool TryGetContentType(string fileName, out string contentType)
    {
        contentType = string.Empty;
        var extension = Path.GetExtension(SafeFileName(fileName));
        return extension.Length > 0 && ContentTypes.TryGetValue(extension, out contentType!);
    }

    /// <summary>
    /// Only the last path segment, without control characters and not longer than 120 characters (the extension is kept).
    /// "file" if nothing usable is left. Used for stored names and for every target path.
    /// </summary>
    public static string SafeFileName(string? name)
    {
        var cleaned = new string((name ?? string.Empty).Where(c => !char.IsControl(c)).ToArray());
        var lastSeparator = cleaned.LastIndexOfAny(['/', '\\']);
        if (lastSeparator >= 0)
            cleaned = cleaned[(lastSeparator + 1)..];
        cleaned = cleaned.Trim();
        if (cleaned.Length == 0 || cleaned.All(c => c == '.'))
            return "file";

        if (cleaned.Length > MaxNameLength)
        {
            var extension = Path.GetExtension(cleaned);
            if (extension.Length > 20)
                extension = string.Empty;
            cleaned = cleaned[..(MaxNameLength - extension.Length)] + extension;
        }
        return cleaned;
    }

    /// <summary>
    /// A job id derived from folder, name, size and modification time. The same file yields the same id, so an import
    /// that was interrupted before the file was moved is recognized by the existing job (no double processing).
    /// </summary>
    public static Guid JobIdFor(string folder, string fileName, long sizeBytes, long lastWriteTicksUtc)
    {
        var text = $"{folder}\n{fileName}\n{sizeBytes}\n{lastWriteTicksUtc}";
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 16));
    }
}
