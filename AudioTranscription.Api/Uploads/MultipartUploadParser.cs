using AudioTranscription.Api.Storage;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace AudioTranscription.Api.Uploads;

/// <summary>
/// Parses a multipart/form-data upload by streaming it straight to disk (S14): unlike ASP.NET Core's
/// <c>IFormFile</c> model binding, this never holds the file's bytes in memory, only one small buffer
/// at a time - the difference between roughly the file's own size in memory growth versus a fixed,
/// small footprint, confirmed by <c>LargeUploadMemoryTests</c> (a 400 MB upload grew the API's working
/// set by ~950 MB with <c>IFormFile</c>, and needs to stay under 200 MB here).
/// </summary>
public static class MultipartUploadParser
{
    private const int HeaderByteCount = 16; // enough for every magic-bytes check MagicBytesValidator does
    private const int BufferSize = 81_920;

    public record Result(
        string? FileName,
        string? FileContentType,
        string? FilePath,
        long FileSizeBytes,
        ReadOnlyMemory<byte> HeaderBytes,
        bool FileTooLarge,
        string? Model,
        string? Language,
        bool Diarize)
    {
        public bool HasFile => FilePath is not null;
    }

    /// <summary>
    /// Reads every part of the request in order. The "file" part is streamed directly to
    /// <paramref name="tempFileStore"/>'s path for <paramref name="jobId"/>; once its running size passes
    /// <paramref name="maxFileSizeBytes"/>, no more of it is written (<see cref="Result.FileTooLarge"/> is
    /// set) but the rest of the request is still drained so the connection stays usable for a clean error
    /// response. Returns null if the request isn't a multipart form (caller should reply 400).
    /// </summary>
    public static async Task<Result?> ParseAsync(
        HttpRequest request, Guid jobId, ITempFileStore tempFileStore, long maxFileSizeBytes, CancellationToken cancellationToken)
    {
        var boundary = GetBoundary(request.ContentType);
        if (boundary is null)
            return null;

        string? fileName = null;
        string? fileContentType = null;
        string? filePath = null;
        long fileSizeBytes = 0;
        var fileTooLarge = false;
        var headerBytes = new byte[HeaderByteCount];
        var headerBytesRead = 0;
        string? model = null;
        string? language = null;
        var diarize = false;

        var reader = new MultipartReader(boundary, request.Body);
        var section = await reader.ReadNextSectionAsync(cancellationToken);
        while (section is not null)
        {
            var contentDisposition = section.GetContentDispositionHeader();
            if (contentDisposition is not null && contentDisposition.IsFileDisposition())
            {
                fileName = contentDisposition.FileName.Value ?? contentDisposition.FileNameStar.Value;
                fileContentType = section.ContentType;
                Directory.CreateDirectory(tempFileStore.StorageDirectory);
                filePath = tempFileStore.GetUploadPath(jobId, fileName ?? "upload");

                await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
                var buffer = new byte[BufferSize];
                int read;
                while ((read = await section.Body.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    if (headerBytesRead < headerBytes.Length)
                    {
                        var toCopy = Math.Min(headerBytes.Length - headerBytesRead, read);
                        Buffer.BlockCopy(buffer, 0, headerBytes, headerBytesRead, toCopy);
                        headerBytesRead += toCopy;
                    }

                    fileSizeBytes += read;
                    if (fileSizeBytes > maxFileSizeBytes)
                    {
                        fileTooLarge = true;
                        continue; // keep draining this section without writing further bytes to disk
                    }

                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
            else if (contentDisposition is not null && contentDisposition.IsFormDisposition())
            {
                var value = await new StreamReader(section.Body).ReadToEndAsync(cancellationToken);
                switch (contentDisposition.Name.Value)
                {
                    case "model": model = value; break;
                    case "language": language = value; break;
                    case "diarize": diarize = bool.TryParse(value, out var parsed) && parsed; break;
                }
            }

            section = await reader.ReadNextSectionAsync(cancellationToken);
        }

        if (fileTooLarge && filePath is not null)
            File.Delete(filePath);

        return new Result(
            fileName, fileContentType, fileTooLarge ? null : filePath, fileSizeBytes,
            headerBytes.AsMemory(0, headerBytesRead), fileTooLarge, model, language, diarize);
    }

    private static string? GetBoundary(string? contentType)
    {
        if (contentType is null || !MediaTypeHeaderValue.TryParse(contentType, out var header))
            return null;
        var boundary = HeaderUtilities.RemoveQuotes(header.Boundary).Value;
        return string.IsNullOrWhiteSpace(boundary) ? null : boundary;
    }
}
