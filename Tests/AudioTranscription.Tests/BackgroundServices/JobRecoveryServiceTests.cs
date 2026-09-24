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

public class JobRecoveryServiceTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly TranscriptionQueue _queue = new();
    private readonly TempFileStore _store;
    private readonly ServiceProvider _provider;

    public JobRecoveryServiceTests()
    {
        _store = new TempFileStore(Options.Create(new UploadOptions { TempStoragePath = _dir.Path }));

        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        _provider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Recovery_EnqueuesPendingAndInterruptedJobsOrderedByCreation()
    {
        var now = DateTime.UtcNow;
        var second = await AddJobAsync(AudioJobStatus.Pending, now.AddMinutes(-2));
        var third = await AddJobAsync(AudioJobStatus.Pending, now.AddMinutes(-1));
        var first = await AddJobAsync(AudioJobStatus.Processing, now.AddMinutes(-3));

        await CreateSut().StartAsync(CancellationToken.None);

        DrainQueue().Select(r => r.JobId).Should().Equal(first.Id, second.Id, third.Id);
    }

    [Fact]
    public async Task Recovery_EnqueuesStoredUploadPath()
    {
        var job = await AddJobAsync(AudioJobStatus.Pending, DateTime.UtcNow);

        await CreateSut().StartAsync(CancellationToken.None);

        DrainQueue().Should().ContainSingle()
            .Which.FilePath.Should().Be(_store.GetUploadPath(job.Id, job.FileName));
    }

    [Fact]
    public async Task Recovery_ResetsInterruptedJobToPending()
    {
        var job = await AddJobAsync(AudioJobStatus.Processing, DateTime.UtcNow);

        await CreateSut().StartAsync(CancellationToken.None);

        (await GetJobAsync(job.Id)).Status.Should().Be(AudioJobStatus.Pending);
    }

    [Theory]
    [InlineData(AudioJobStatus.Pending)]
    [InlineData(AudioJobStatus.Processing)]
    public async Task Recovery_WhenUploadMissing_FailsJobWithoutEnqueuing(AudioJobStatus status)
    {
        var job = await AddJobAsync(status, DateTime.UtcNow, createUpload: false);

        await CreateSut().StartAsync(CancellationToken.None);

        DrainQueue().Should().BeEmpty();
        var stored = await GetJobAsync(job.Id);
        stored.Status.Should().Be(AudioJobStatus.Failed);
        stored.ErrorMessage.Should().Be(JobRecoveryService.SourceFileMissingMessage);
        stored.CompletedAtUtc.Should().NotBeNull();
    }

    [Theory]
    [InlineData(AudioJobStatus.Completed)]
    [InlineData(AudioJobStatus.Failed)]
    public async Task Recovery_IgnoresFinishedJobs(AudioJobStatus status)
    {
        var job = await AddJobAsync(status, DateTime.UtcNow);

        await CreateSut().StartAsync(CancellationToken.None);

        DrainQueue().Should().BeEmpty();
        (await GetJobAsync(job.Id)).Status.Should().Be(status);
    }

    private JobRecoveryService CreateSut() => new(
        _provider.GetRequiredService<IServiceScopeFactory>(),
        _queue,
        _store,
        NullLogger<JobRecoveryService>.Instance);

    private async Task<AudioJob> AddJobAsync(AudioJobStatus status, DateTime createdAtUtc, bool createUpload = true)
    {
        var job = new AudioJob
        {
            FileName = "meeting.mp3",
            ContentType = "audio/mpeg",
            Status = status,
            CreatedAtUtc = createdAtUtc
        };

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AudioJobs.Add(job);
        await db.SaveChangesAsync();

        if (createUpload)
            _dir.CreateFile(Path.GetFileName(_store.GetUploadPath(job.Id, job.FileName)));

        return job;
    }

    private async Task<AudioJob> GetJobAsync(Guid jobId)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AudioJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
    }

    private List<TranscriptionJobRequest> DrainQueue()
    {
        var requests = new List<TranscriptionJobRequest>();
        while (_queue.TryRead(out var request))
            requests.Add(request);
        return requests;
    }

    public void Dispose()
    {
        _provider.Dispose();
        _dir.Dispose();
    }
}
