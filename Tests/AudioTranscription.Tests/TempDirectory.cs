namespace AudioTranscription.Tests;

/// <summary>
/// Creates a unique temporary directory for a test and removes it afterwards.
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "audio-tests-" + Guid.NewGuid().ToString("N"));

    public TempDirectory() => Directory.CreateDirectory(Path);

    public string CreateFile(string fileName, DateTime? lastWriteTimeUtc = null)
    {
        var filePath = System.IO.Path.Combine(Path, fileName);
        File.WriteAllBytes(filePath, [0x49, 0x44, 0x33]);
        if (lastWriteTimeUtc is not null)
            File.SetLastWriteTimeUtc(filePath, lastWriteTimeUtc.Value);
        return filePath;
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}
