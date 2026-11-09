using AudioTranscription.Api.Access;
using AudioTranscription.Api.BackgroundServices;
using AudioTranscription.Api.Configuration;
using AudioTranscription.Api.Storage;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AudioTranscription.Tests.BackgroundServices;

/// <summary>S30: automatic deletion of old transcripts and audio files, with a simulated clock.</summary>
public class RetentionServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc);

    private readonly TempDirectory _dir = new();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Mock<IJobNotifier> _notifier = new();
    private readonly Mock<ILogger<RetentionService>> _logger = new();
    private ServiceProvider? _provider;
    private TempFileStore? _store;

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private RetentionService CreateSut(int transcriptDays = 0, int audioDays = 0)
    {
        var upload = Options.Create(new UploadOptions { TempStoragePath = _dir.Path, DeleteAfterTranscription = false });
        _store = new TempFileStore(upload);
        _notifier.Setup(n => n.GetRecipientsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(["owner-id"]);

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddSingleton(_notifier.Object);
        _provider = services.BuildServiceProvider();

        return new RetentionService(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            _store,
            Options.Create(new RetentionOptions { TranscriptDays = transcriptDays, AudioDays = audioDays }),
            new FixedTimeProvider(Now),
            _logger.Object);
    }

    private async Task<(Guid Id, string File)> AddJobAsync(
        int ageDays,
        AudioJobStatus status = AudioJobStatus.Completed,
        bool locked = false,
        bool completedAtMissing = false)
    {
        using var scope = _provider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = new AudioJob
        {
            FileName = "meeting.mp3",
            ContentType = "audio/mpeg",
            Status = status,
            OwnerId = "owner-id",
            RetentionLocked = locked,
            CreatedAtUtc = Now.AddDays(-ageDays - 1),
            CompletedAtUtc = completedAtMissing ? null : Now.AddDays(-ageDays),
        };
        db.AudioJobs.Add(job);
        await db.SaveChangesAsync();
        var file = _dir.CreateFile(Path.GetFileName(_store!.GetUploadPath(job.Id, job.FileName)));
        return (job.Id, file);
    }

    private async Task<bool> JobExistsAsync(Guid id)
    {
        using var scope = _provider!.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AudioJobs.AnyAsync(j => j.Id == id);
    }

    [Fact]
    public async Task OldFinishedJob_IsDeletedWithItsUpload_AndRecentOneStays()
    {
        var sut = CreateSut(transcriptDays: 30);
        var old = await AddJobAsync(ageDays: 31);
        var recent = await AddJobAsync(ageDays: 29);

        await sut.RunOnceAsync(CancellationToken.None);

        (await JobExistsAsync(old.Id)).Should().BeFalse();
        File.Exists(old.File).Should().BeFalse();
        (await JobExistsAsync(recent.Id)).Should().BeTrue();
        File.Exists(recent.File).Should().BeTrue();
    }

    [Theory]
    [InlineData(AudioJobStatus.Failed)]
    [InlineData(AudioJobStatus.Cancelled)]
    public async Task OldFailedOrCancelledJob_IsDeletedToo(AudioJobStatus status)
    {
        var sut = CreateSut(transcriptDays: 30);
        var old = await AddJobAsync(ageDays: 40, status);

        await sut.RunOnceAsync(CancellationToken.None);

        (await JobExistsAsync(old.Id)).Should().BeFalse();
    }

    [Theory]
    [InlineData(AudioJobStatus.Pending)]
    [InlineData(AudioJobStatus.Processing)]
    public async Task OpenJobs_AreNeverDeleted(AudioJobStatus status)
    {
        var sut = CreateSut(transcriptDays: 1, audioDays: 1);
        var open = await AddJobAsync(ageDays: 400, status);

        await sut.RunOnceAsync(CancellationToken.None);

        (await JobExistsAsync(open.Id)).Should().BeTrue();
        File.Exists(open.File).Should().BeTrue();
    }

    [Fact]
    public async Task LockedJob_IsKept_WithItsUpload()
    {
        var sut = CreateSut(transcriptDays: 30, audioDays: 30);
        var locked = await AddJobAsync(ageDays: 365, locked: true);

        await sut.RunOnceAsync(CancellationToken.None);

        (await JobExistsAsync(locked.Id)).Should().BeTrue();
        File.Exists(locked.File).Should().BeTrue();
    }

    [Fact]
    public async Task JobWithoutCompletionTime_IsJudgedByItsCreationTime()
    {
        var sut = CreateSut(transcriptDays: 30);
        var failed = await AddJobAsync(ageDays: 40, AudioJobStatus.Failed, completedAtMissing: true);

        await sut.RunOnceAsync(CancellationToken.None);

        (await JobExistsAsync(failed.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task AudioRetention_RemovesOnlyTheUpload_AndKeepsTheTranscript()
    {
        var sut = CreateSut(transcriptDays: 0, audioDays: 7);
        var old = await AddJobAsync(ageDays: 10);
        var recent = await AddJobAsync(ageDays: 3);

        await sut.RunOnceAsync(CancellationToken.None);

        File.Exists(old.File).Should().BeFalse();
        (await JobExistsAsync(old.Id)).Should().BeTrue("only the audio expires");
        File.Exists(recent.File).Should().BeTrue();
    }

    [Fact]
    public async Task TheTranscriptOutlivesTheAudio_WhenBothAreConfigured()
    {
        var sut = CreateSut(transcriptDays: 90, audioDays: 7);
        var job = await AddJobAsync(ageDays: 30);

        await sut.RunOnceAsync(CancellationToken.None);

        File.Exists(job.File).Should().BeFalse();
        (await JobExistsAsync(job.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task ByDefault_NothingIsDeleted()
    {
        var sut = CreateSut();
        var ancient = await AddJobAsync(ageDays: 3650);

        await sut.RunOnceAsync(CancellationToken.None);

        (await JobExistsAsync(ancient.Id)).Should().BeTrue();
        File.Exists(ancient.File).Should().BeTrue();
    }

    [Fact]
    public async Task RunningTwice_IsIdempotent()
    {
        var sut = CreateSut(transcriptDays: 30, audioDays: 7);
        var old = await AddJobAsync(ageDays: 40);

        await sut.RunOnceAsync(CancellationToken.None);
        var second = await Record.ExceptionAsync(() => sut.RunOnceAsync(CancellationToken.None));

        second.Should().BeNull();
        (await JobExistsAsync(old.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task DeletedJob_IsAnnouncedToItsOwnerAndSharedUsers()
    {
        var sut = CreateSut(transcriptDays: 30);
        var old = await AddJobAsync(ageDays: 40);

        await sut.RunOnceAsync(CancellationToken.None);

        _notifier.Verify(n => n.SendAsync(
            It.Is<IReadOnlyList<string>>(r => r.SequenceEqual(new[] { "owner-id" })),
            "JobDeleted", old.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OneFailingJob_DoesNotStopTheRun_AndTheLogContainsNoContent()
    {
        var sut = CreateSut(transcriptDays: 30);
        var first = await AddJobAsync(ageDays: 40);
        var second = await AddJobAsync(ageDays: 41);
        _notifier.SetupSequence(n => n.GetRecipientsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("disk gone"))
            .ReturnsAsync(["owner-id"]);

        await sut.RunOnceAsync(CancellationToken.None);

        var remaining = new[] { first.Id, second.Id }.Select(JobExistsAsync).Select(t => t.Result).Count(exists => exists);
        remaining.Should().Be(1, "one job failed and is retried at the next run, the other was deleted");
        _logger.Invocations
            .Where(i => i.Method.Name == nameof(ILogger.Log))
            .Select(i => i.Arguments[2]?.ToString())
            .Should().NotContain(m => m!.Contains("meeting.mp3"), "file names can contain personal data");
    }

    public void Dispose()
    {
        _provider?.Dispose();
        _dir.Dispose();
    }
}
