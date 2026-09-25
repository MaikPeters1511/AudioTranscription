using AudioTranscription.Api.Configuration;
using AudioTranscription.Api.Storage;
using AudioTranscription.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace AudioTranscription.Tests.Storage;

public class TempFileStoreTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    private TempFileStore CreateSut(bool deleteAfterTranscription = true) =>
        new(Options.Create(new UploadOptions
        {
            TempStoragePath = _dir.Path,
            DeleteAfterTranscription = deleteAfterTranscription
        }));

    [Fact]
    public void StorageDirectory_WithAbsolutePath_ReturnsPathUnchanged()
    {
        CreateSut().StorageDirectory.Should().Be(_dir.Path);
    }

    [Fact]
    public void StorageDirectory_WithRelativePath_ResolvesAgainstCurrentDirectory()
    {
        var sut = new TempFileStore(Options.Create(new UploadOptions { TempStoragePath = "temp-uploads" }));

        sut.StorageDirectory.Should().Be(Path.Combine(Directory.GetCurrentDirectory(), "temp-uploads"));
    }

    [Theory]
    [InlineData("meeting.mp3", ".mp3")]
    [InlineData("Interview.Final.OGG", ".OGG")]
    [InlineData("no-extension", "")]
    public void GetUploadPath_CombinesJobIdAndOriginalExtension(string originalFileName, string expectedExtension)
    {
        var jobId = Guid.NewGuid();

        var path = CreateSut().GetUploadPath(jobId, originalFileName);

        path.Should().Be(Path.Combine(_dir.Path, $"{jobId}{expectedExtension}"));
    }

    [Theory]
    [InlineData(AudioJobStatus.Completed)]
    [InlineData(null)] // job no longer exists
    public void CleanupAfterProcessing_AfterSuccessOrWithoutJob_DeletesFile(AudioJobStatus? outcome)
    {
        var file = _dir.CreateFile($"{Guid.NewGuid()}.mp3");

        CreateSut(deleteAfterTranscription: true).CleanupAfterProcessing(file, outcome);

        File.Exists(file).Should().BeFalse();
    }

    [Theory]
    [InlineData(AudioJobStatus.Failed)]
    [InlineData(AudioJobStatus.Cancelled)]
    public void CleanupAfterProcessing_AfterFailureOrCancel_KeepsFileForRetry(AudioJobStatus outcome)
    {
        var file = _dir.CreateFile($"{Guid.NewGuid()}.mp3");

        CreateSut(deleteAfterTranscription: true).CleanupAfterProcessing(file, outcome);

        File.Exists(file).Should().BeTrue();
    }

    [Fact]
    public void CleanupAfterProcessing_WhenDeleteDisabled_KeepsFile()
    {
        var file = _dir.CreateFile($"{Guid.NewGuid()}.mp3");

        CreateSut(deleteAfterTranscription: false).CleanupAfterProcessing(file, AudioJobStatus.Completed);

        File.Exists(file).Should().BeTrue();
    }

    [Fact]
    public void CleanupAfterProcessing_WhenFileMissing_DoesNotThrow()
    {
        var missing = Path.Combine(_dir.Path, "does-not-exist.mp3");

        var act = () => CreateSut().CleanupAfterProcessing(missing, AudioJobStatus.Completed);

        act.Should().NotThrow();
    }

    [Fact]
    public void DeleteUpload_DeletesEvenWhenUploadsAreKept()
    {
        var file = _dir.CreateFile($"{Guid.NewGuid()}.mp3");

        CreateSut(deleteAfterTranscription: false).DeleteUpload(file);

        File.Exists(file).Should().BeFalse();
    }

    public void Dispose() => _dir.Dispose();
}
