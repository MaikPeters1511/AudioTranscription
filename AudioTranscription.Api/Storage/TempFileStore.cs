using AudioTranscription.Api.Configuration;
using AudioTranscription.Domain.Enums;
using Microsoft.Extensions.Options;

namespace AudioTranscription.Api.Storage;

public class TempFileStore(IOptions<UploadOptions> options) : ITempFileStore
{
    private readonly UploadOptions _options = options.Value;

    public string StorageDirectory { get; } =
        Path.Combine(Directory.GetCurrentDirectory(), options.Value.TempStoragePath);

    public string GetUploadPath(Guid jobId, string originalFileName) =>
        Path.Combine(StorageDirectory, $"{jobId}{Path.GetExtension(originalFileName)}");

    // File.Delete does not throw for a missing file
    public void DeleteUpload(string filePath) => File.Delete(filePath);

    public void CleanupAfterProcessing(string filePath, AudioJobStatus? outcome)
    {
        // Failed/cancelled jobs keep their upload for a retry (D2); it is removed by
        // DELETE, a successful retry or the orphan cleanup after OrphanedFileRetentionHours
        var jobFinishedOrGone = outcome is null or AudioJobStatus.Completed;
        if (!_options.DeleteAfterTranscription || !jobFinishedOrGone)
            return;

        // File.Delete does not throw for a missing file
        File.Delete(filePath);
    }
}
