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
    private HookedFileStore? _store;

    /// <summary>The real store, with a hook to run code (or fail) when an upload is about to be deleted.</summary>
    private sealed class HookedFileStore(TempFileStore inner) : ITempFileStore
    {
        public Action<string>? DeleteUploadHook { get; set; }
        public string StorageDirectory => inner.StorageDirectory;
        public string GetUploadPath(Guid jobId, string originalFileName) => inner.GetUploadPath(jobId, originalFileName);
        public void CleanupAfterProcessing(string filePath, AudioJobStatus? outcome) => inner.CleanupAfterProcessing(filePath, outcome);

        public void DeleteUpload(string filePath)
        {
            DeleteUploadHook?.Invoke(filePath);
            inner.DeleteUpload(filePath);
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private RetentionService CreateSut(int transcriptDays = 0, int audioDays = 0, int maxJobsPerRun = 500, int batchSize = 500)
    {
        var upload = Options.Create(new UploadOptions { TempStoragePath = _dir.Path, DeleteAfterTranscription = false });
        _store = new HookedFileStore(new TempFileStore(upload));
        _notifier.Setup(n => n.GetRecipientsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(["owner-id"]);

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddSingleton(_notifier.Object);
        _provider = services.BuildServiceProvider();

        return new RetentionService(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            _store,
            Options.Create(new RetentionOptions { TranscriptDays = transcriptDays, AudioDays = audioDays, MaxJobsPerRun = maxJobsPerRun }),
            new FixedTimeProvider(Now),
            _logger.Object) { BatchSize = batchSize };
    }

    private async Task<(Guid Id, string File)> AddJobAsync(
        int ageDays,
        AudioJobStatus status = AudioJobStatus.Completed,
        bool locked = false,
        bool completedAtMissing = false,
        string? ownerId = "owner-id",
        double ageDaysFraction = 0)
    {
        using var scope = _provider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = new AudioJob
        {
            FileName = "meeting.mp3",
            ContentType = "audio/mpeg",
            Status = status,
            OwnerId = ownerId,
            RetentionLocked = locked,
            CreatedAtUtc = Now.AddDays(-ageDays - 1),
            CompletedAtUtc = completedAtMissing ? null : Now.AddDays(-ageDays).AddDays(-ageDaysFraction),
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

    // ---- boundary

    [Fact]
    public async Task ExactlyAtTheCutoff_IsKept_AndOneTickOlderIsDeleted()
    {
        var sut = CreateSut(transcriptDays: 30);
        var atCutoff = await AddJobAsync(ageDays: 30);
        var older = await AddJobAsync(ageDays: 30, ageDaysFraction: 1d / 86_400);

        await sut.RunOnceAsync(CancellationToken.None);

        (await JobExistsAsync(atCutoff.Id)).Should().BeTrue("exactly N days old is not yet older than N days");
        (await JobExistsAsync(older.Id)).Should().BeFalse();
    }

    // ---- jobs nobody owns (S19) cannot be locked, so retention must not touch them

    [Fact]
    public async Task JobsWithoutAnOwner_AreNeverDeleted()
    {
        var sut = CreateSut(transcriptDays: 1, audioDays: 1);
        var orphan = await AddJobAsync(ageDays: 400, ownerId: null);

        await sut.RunOnceAsync(CancellationToken.None);

        (await JobExistsAsync(orphan.Id)).Should().BeTrue("an admin has to assign it first, then its owner can lock it");
        File.Exists(orphan.File).Should().BeTrue();
    }

    // ---- K1: the job may change between the selection and the deletion

    [Theory]
    [InlineData(AudioJobStatus.Pending)]
    [InlineData(AudioJobStatus.Processing)]
    public async Task AJobThatIsRetriedMeanwhile_IsNotDeleted_NorIsItsUpload(AudioJobStatus reopenedAs)
    {
        var sut = CreateSut(transcriptDays: 30);
        var job = await AddJobAsync(ageDays: 40, AudioJobStatus.Failed);
        _notifier.Setup(n => n.GetRecipientsAsync(job.Id, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                // The owner presses "retry" after retention listed the job but before it deleted it
                await ChangeJobAsync(job.Id, j => { j.Status = reopenedAs; j.CompletedAtUtc = null; });
                return ["owner-id"];
            });

        await sut.RunOnceAsync(CancellationToken.None);

        (await JobExistsAsync(job.Id)).Should().BeTrue();
        File.Exists(job.File).Should().BeTrue("the retry needs the upload");
    }

    [Fact]
    public async Task AJobThatIsLockedMeanwhile_IsNotDeleted()
    {
        var sut = CreateSut(transcriptDays: 30);
        var job = await AddJobAsync(ageDays: 40);
        _notifier.Setup(n => n.GetRecipientsAsync(job.Id, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await ChangeJobAsync(job.Id, j => j.RetentionLocked = true);
                return ["owner-id"];
            });

        await sut.RunOnceAsync(CancellationToken.None);

        (await JobExistsAsync(job.Id)).Should().BeTrue();
        File.Exists(job.File).Should().BeTrue();
    }

    private async Task ChangeJobAsync(Guid id, Action<AudioJob> change)
    {
        using var scope = _provider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        change(await db.AudioJobs.SingleAsync(j => j.Id == id));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task AudioRetention_ReChecksJustBeforeDeletingTheFile()
    {
        var sut = CreateSut(audioDays: 7);
        var job = await AddJobAsync(ageDays: 10, AudioJobStatus.Failed);
        // Locked while the run is busy with an earlier job: the file must stay
        var earlier = await AddJobAsync(ageDays: 20, AudioJobStatus.Failed);
        _store!.DeleteUploadHook = path =>
        {
            if (path == earlier.File)
                ChangeJobAsync(job.Id, j => j.RetentionLocked = true).GetAwaiter().GetResult();
        };

        await sut.RunOnceAsync(CancellationToken.None);

        File.Exists(earlier.File).Should().BeFalse();
        File.Exists(job.File).Should().BeTrue("it was locked after the selection");
    }

    // ---- H3: limit and visibility of a mass deletion

    [Fact]
    public async Task AtMostMaxJobsPerRun_AreDeleted_OldestFirst_AndTheRestFollowsInTheNextRun()
    {
        var sut = CreateSut(transcriptDays: 30, maxJobsPerRun: 2);
        var oldest = await AddJobAsync(ageDays: 400);
        var middle = await AddJobAsync(ageDays: 300);
        var newest = await AddJobAsync(ageDays: 40);

        await sut.RunOnceAsync(CancellationToken.None);
        (await JobExistsAsync(oldest.Id)).Should().BeFalse();
        (await JobExistsAsync(middle.Id)).Should().BeFalse();
        (await JobExistsAsync(newest.Id)).Should().BeTrue();

        await sut.RunOnceAsync(CancellationToken.None);
        (await JobExistsAsync(newest.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task TheNumberOfJobsAboutToBeDeleted_IsLoggedBeforeDeleting()
    {
        var sut = CreateSut(transcriptDays: 30);
        await AddJobAsync(ageDays: 40);
        await AddJobAsync(ageDays: 41);

        await sut.RunOnceAsync(CancellationToken.None);

        _logger.Invocations
            .Where(i => i.Method.Name == nameof(ILogger.Log) && (LogLevel)i.Arguments[0] == LogLevel.Warning)
            .Select(i => i.Arguments[2]?.ToString())
            .Should().Contain(m => m!.Contains('2') && m.Contains("job"), "an operator has to see how many jobs a run is going to delete");
    }

    // ---- S30-F2: selection in batches

    [Fact]
    public async Task TheWarning_NamesTheTotalNumberOfExpiredJobs_NotOnlyTheBatch()
    {
        var sut = CreateSut(transcriptDays: 30, maxJobsPerRun: 2);
        for (var i = 0; i < 5; i++)
            await AddJobAsync(ageDays: 40 + i);

        await sut.RunOnceAsync(CancellationToken.None);

        _logger.Invocations
            .Where(i => i.Method.Name == nameof(ILogger.Log) && (LogLevel)i.Arguments[0] == LogLevel.Warning)
            .Select(i => i.Arguments[2]?.ToString())
            .Should().Contain(m => m!.Contains("2 of 5"), "the operator sees the backlog, not just this run's share");
    }

    [Fact]
    public async Task TheAudioPass_LooksOnlyAtJobsBetweenTheAudioAndTheTranscriptLimit()
    {
        var sut = CreateSut(transcriptDays: 30, audioDays: 7, maxJobsPerRun: 1);
        var oldest = await AddJobAsync(ageDays: 100);   // deleted with its upload by the transcript pass
        var waiting = await AddJobAsync(ageDays: 90);   // past the transcript limit, but over the per-run limit: not the audio pass' business
        var between = await AddJobAsync(ageDays: 20);   // audio expired, transcript not

        await sut.RunOnceAsync(CancellationToken.None);

        (await JobExistsAsync(oldest.Id)).Should().BeFalse();
        File.Exists(waiting.File).Should().BeTrue("the transcript pass owns jobs past the transcript limit and removes them at a later run");
        File.Exists(between.File).Should().BeFalse();
        (await JobExistsAsync(between.Id)).Should().BeTrue();

        await sut.RunOnceAsync(CancellationToken.None);
        (await JobExistsAsync(waiting.Id)).Should().BeFalse();
        File.Exists(waiting.File).Should().BeFalse();
    }

    [Fact]
    public async Task TheAudioPass_WorksThroughAllBatches()
    {
        var sut = CreateSut(transcriptDays: 365, audioDays: 7, batchSize: 3);
        var files = new List<string>();
        for (var i = 0; i < 8; i++)
            files.Add((await AddJobAsync(ageDays: 10 + i)).File);
        var recent = await AddJobAsync(ageDays: 2);

        await sut.RunOnceAsync(CancellationToken.None);

        files.Should().OnlyContain(f => !File.Exists(f), "every job past the audio limit is handled, however many batches that takes");
        File.Exists(recent.File).Should().BeTrue();
    }

    [Fact]
    public async Task TheAudioPass_WithoutATranscriptLimit_StillCoversEveryOldJob()
    {
        var sut = CreateSut(audioDays: 7, batchSize: 2);
        var files = new List<string>();
        for (var i = 0; i < 5; i++)
            files.Add((await AddJobAsync(ageDays: 400 + i)).File);

        await sut.RunOnceAsync(CancellationToken.None);

        files.Should().OnlyContain(f => !File.Exists(f));
    }

    // ---- M2/M3: failures

    [Fact]
    public async Task WhenTheUploadCannotBeDeleted_TheJobIsStillGoneAndAnnounced_WithoutLoggingThePath()
    {
        var sut = CreateSut(transcriptDays: 30);
        var job = await AddJobAsync(ageDays: 40);
        _store!.DeleteUploadHook = _ => throw new IOException($"Sharing violation on {job.File}");

        await sut.RunOnceAsync(CancellationToken.None);

        (await JobExistsAsync(job.Id)).Should().BeFalse();
        _notifier.Verify(n => n.SendAsync(It.IsAny<IReadOnlyList<string>>(), "JobDeleted", job.Id, It.IsAny<CancellationToken>()), Times.Once);
        AllLogText().Should().NotContain(job.File).And.NotContain(Path.GetFileName(job.File));
    }

    [Fact]
    public async Task AnUnexpectedErrorWithOneJob_DoesNotStopTheRest_NorTheAudioPart()
    {
        var sut = CreateSut(transcriptDays: 30, audioDays: 7);
        var failing = await AddJobAsync(ageDays: 40);
        var healthy = await AddJobAsync(ageDays: 41);
        var audioOnly = await AddJobAsync(ageDays: 10);
        _notifier.Setup(n => n.GetRecipientsAsync(failing.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("db timeout"));

        await sut.RunOnceAsync(CancellationToken.None);

        (await JobExistsAsync(failing.Id)).Should().BeTrue();
        (await JobExistsAsync(healthy.Id)).Should().BeFalse();
        File.Exists(audioOnly.File).Should().BeFalse("the audio part still ran");
    }

    [Fact]
    public async Task ANotificationFailure_DoesNotCountAsAFailedDeletion()
    {
        var sut = CreateSut(transcriptDays: 30);
        var job = await AddJobAsync(ageDays: 40);
        _notifier.Setup(n => n.SendAsync(It.IsAny<IReadOnlyList<string>>(), "JobDeleted", job.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hub down"));

        var error = await Record.ExceptionAsync(() => sut.RunOnceAsync(CancellationToken.None));

        error.Should().BeNull();
        (await JobExistsAsync(job.Id)).Should().BeFalse();
    }

    private string AllLogText() => string.Join("\n", _logger.Invocations
        .Where(i => i.Method.Name == nameof(ILogger.Log))
        .Select(i => $"{i.Arguments[2]} {i.Arguments[3]}"));

    public void Dispose()
    {
        _provider?.Dispose();
        _dir.Dispose();
    }
}
