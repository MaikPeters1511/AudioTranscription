namespace AudioTranscription.Api.Storage;

/// <summary>
/// Central place for the lifecycle of uploaded audio files in temp storage.
/// All decisions about whether an upload is kept or deleted live here.
/// </summary>
public interface ITempFileStore
{
    /// <summary>
    /// Absolute path of the directory uploads are stored in.
    /// </summary>
    string StorageDirectory { get; }

    /// <summary>
    /// Path an upload is stored under: "{jobId}{extension of the original file name}".
    /// The path is derived, not persisted, so it can be rebuilt from the job at any time.
    /// </summary>
    string GetUploadPath(Guid jobId, string originalFileName);

    /// <summary>
    /// Called once a job has finished processing (successfully or not).
    /// Deletes the upload unless configuration says to keep it.
    /// Throws if the file exists but cannot be deleted; callers decide how to handle that.
    /// </summary>
    void CleanupAfterProcessing(string filePath);
}
