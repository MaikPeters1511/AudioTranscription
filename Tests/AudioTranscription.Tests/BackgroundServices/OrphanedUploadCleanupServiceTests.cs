using AudioTranscription.Api.BackgroundServices;
using AudioTranscription.Api.Configuration;
using AudioTranscription.Api.Storage;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AudioTranscription.Tests.BackgroundServices;

public class OrphanedUploadCleanupServiceTests : IDisposable
{
    private static readonly DateTime Old = DateTime.UtcNow.AddHours(-48);

    private readonly TempDirectory _dir = new();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private ServiceProvider? _provider;

    [Fact]
    public async Task Cleanup_DeletesOldFileWithoutJob()
    {
        var sut = CreateSut();
        var orphan = _dir.CreateFile($"{Guid.NewGuid()}.mp3", Old);

        await sut.StartAsync(CancellationToken.None);

        File.Exists(orphan).Should().BeFalse();
    }

    [Fact]
    public async Task Cleanup_DeletesOldFileWithoutJobIdInName()
    {
        var sut = CreateSut();
        var leftoverWav = _dir.CreateFile($"{Guid.NewGuid()}_16khz.wav", Old);

        await sut.StartAsync(CancellationToken.None);

        File.Exists(leftoverWav).Should().BeFalse();
    }

    [Theory]
    [InlineData(AudioJobStatus.Pending)]
    [InlineData(AudioJobStatus.Processing)]
    public async Task Cleanup_KeepsFileOfOpenJob(AudioJobStatus status)
    {
        var sut = CreateSut();
        var jobId = await AddJobAsync(status);
        var file = _dir.CreateFile($"{jobId}.mp3", Old);

        await sut.StartAsync(CancellationToken.None);

        File.Exists(file).Should().BeTrue();
    }

    [Fact]
    public async Task Cleanup_KeepsRecentFileWithoutJob()
    {
        var sut = CreateSut();
        var justUploaded = _dir.CreateFile($"{Guid.NewGuid()}.mp3", DateTime.UtcNow.AddMinutes(-5));

        await sut.StartAsync(CancellationToken.None);

        File.Exists(justUploaded).Should().BeTrue();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Cleanup_FileOfFinishedJob_FollowsDeleteAfterTranscription(
        bool deleteAfterTranscription, bool expectedToExist)
    {
        var sut = CreateSut(deleteAfterTranscription);
        var jobId = await AddJobAsync(AudioJobStatus.Completed);
        var file = _dir.CreateFile($"{jobId}.mp3", Old);

        await sut.StartAsync(CancellationToken.None);

        File.Exists(file).Should().Be(expectedToExist);
    }

    [Fact]
    public async Task Cleanup_WhenStorageDirectoryMissing_DoesNotThrow()
    {
        var sut = CreateSut();
        _dir.Dispose();

        var exception = await Record.ExceptionAsync(() => sut.StartAsync(CancellationToken.None));

        exception.Should().BeNull();
    }

    private OrphanedUploadCleanupService CreateSut(bool deleteAfterTranscription = true)
    {
        var options = Options.Create(new UploadOptions
        {
            TempStoragePath = _dir.Path,
            DeleteAfterTranscription = deleteAfterTranscription,
            OrphanedFileRetentionHours = 24
        });

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        _provider = services.BuildServiceProvider();

        return new OrphanedUploadCleanupService(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            new TempFileStore(options),
            options,
            TimeProvider.System,
            NullLogger<OrphanedUploadCleanupService>.Instance);
    }

    private async Task<Guid> AddJobAsync(AudioJobStatus status)
    {
        using var scope = _provider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = new AudioJob { FileName = "a.mp3", ContentType = "audio/mpeg", Status = status };
        db.AudioJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    public void Dispose()
    {
        _provider?.Dispose();
        _dir.Dispose();
    }
}
