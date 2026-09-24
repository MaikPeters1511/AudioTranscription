namespace AudioTranscription.Api.Validation;

/// <summary>
/// Validates audio files by checking magic bytes (file signatures) rather than trusting file extensions.
/// </summary>
public static class MagicBytesValidator
{
    private static readonly Dictionary<string, byte[][]> MagicBytes = new()
    {
        // MP3: ID3 tag or MPEG sync bytes
        ["audio/mpeg"] =
        [
            "ID3"u8.ToArray(),
            [0xFF, 0xFB],
            [0xFF, 0xF3],
            [0xFF, 0xF2]
        ],
        // WAV: RIFF....WAVE
        ["audio/wav"] = [[0x52, 0x49, 0x46, 0x46]],       // RIFF
        ["audio/x-wav"] = [[0x52, 0x49, 0x46, 0x46]],     // RIFF
        // M4A/MP4: ftyp
        ["audio/mp4"] = [[0x00, 0x00, 0x00]],              // ftyp box (variable offset)
        ["audio/x-m4a"] = [[0x00, 0x00, 0x00]],
        // OGG: OggS
        ["audio/ogg"] = [[0x4F, 0x67, 0x67, 0x53]],        // OggS
        // WebM (MediaRecorder default in Chrome/Firefox, S12): EBML header
        ["audio/webm"] = [[0x1A, 0x45, 0xDF, 0xA3]],
        // Video containers (S14); only the audio track is extracted (ffmpeg "-vn")
        ["video/mp4"] = [[0x00, 0x00, 0x00]],              // ftyp box (variable offset), like audio/mp4
        ["video/quicktime"] = [[0x00, 0x00, 0x00]],        // .mov: also an ftyp/moov-based container
        ["video/webm"] = [[0x1A, 0x45, 0xDF, 0xA3]],       // EBML header, like audio/webm
        ["video/x-matroska"] = [[0x1A, 0x45, 0xDF, 0xA3]]  // .mkv: also EBML-based (Matroska)
    };

    /// <summary>
    /// Validates that the file content matches the expected magic bytes for the given content type.
    /// </summary>
    public static bool IsValid(string contentType, Stream fileStream)
    {
        if (!MagicBytes.TryGetValue(contentType, out var signatures))
            return false;

        var originalPosition = fileStream.Position;
        try
        {
            var buffer = new byte[12];
            fileStream.Position = 0;
            var bytesRead = fileStream.Read(buffer, 0, buffer.Length);

            if (bytesRead < 2)
                return false;

            foreach (var signature in signatures)
            {
                if (signature.Length <= bytesRead && buffer.AsSpan(0, signature.Length).SequenceEqual(signature))
                    return true;
            }

            // Special check for M4A/MP4/MOV: look for 'ftyp' at offset 4
            if (contentType is "audio/mp4" or "audio/x-m4a" or "video/mp4" or "video/quicktime" && bytesRead >= 8)
            {
                var ftypSignature = "ftyp"u8;
                if (buffer.AsSpan(4, 4).SequenceEqual(ftypSignature))
                    return true;
            }

            return false;
        }
        finally
        {
            fileStream.Position = originalPosition;
        }
    }
}
