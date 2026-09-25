using AudioTranscription.Domain.Enums;

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
    /// Called once the worker is done with a job. Deletes the upload after a successful
    /// transcription (or when the job no longer exists) unless configuration keeps uploads.
    /// Failed and cancelled jobs keep their upload so they can be retried (decision D2).
    /// Throws if the file exists but cannot be deleted; callers decide how to handle that.
    /// </summary>
    /// <param name="outcome">Final job status, or null if the job does not exist.</param>
    void CleanupAfterProcessing(string filePath, AudioJobStatus? outcome);

    /// <summary>Deletes an upload regardless of configuration, e.g. when its job is deleted.</summary>
    void DeleteUpload(string filePath);
}
