using AudioTranscription.Api.Configuration;
using Microsoft.Extensions.Options;

namespace AudioTranscription.Api.Storage;

public class TempFileStore(IOptions<UploadOptions> options) : ITempFileStore
{
    private readonly UploadOptions _options = options.Value;

    public string StorageDirectory { get; } =
        Path.Combine(Directory.GetCurrentDirectory(), options.Value.TempStoragePath);

    public string GetUploadPath(Guid jobId, string originalFileName) =>
        Path.Combine(StorageDirectory, $"{jobId}{Path.GetExtension(originalFileName)}");

    public void CleanupAfterProcessing(string filePath)
    {
        if (!_options.DeleteAfterTranscription)
            return;

        // File.Delete does not throw for a missing file
        File.Delete(filePath);
    }
}
