using AudioTranscription.Api.BackgroundServices;
using AudioTranscription.Api.Configuration;
using AudioTranscription.Api.Hubs;
using AudioTranscription.Api.Storage;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using AudioTranscription.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AudioTranscription.Tests.BackgroundServices;

public class TranscriptionWorkerTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly Mock<ITranscriptionService> _transcription = new();
    private readonly Mock<ILogger<TranscriptionWorker>> _logger = new();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private ServiceProvider? _provider;

    [Fact]
    public async Task ProcessJob_WhenTranscriptionFails_DeletesUpload()
    {
        var (worker, jobId, file) = await ArrangeAsync(deleteAfterTranscription: true);
        TranscriptionThrows(new InvalidOperationException("ffmpeg missing"));

        await worker.ProcessJobAsync(new TranscriptionJobRequest(jobId, file), CancellationToken.None);

        File.Exists(file).Should().BeFalse();
        (await GetJobAsync(jobId)).Status.Should().Be(AudioJobStatus.Failed);
    }

    [Fact]
    public async Task ProcessJob_WhenTranscriptionSucceeds_DeletesUpload()
    {
        var (worker, jobId, file) = await ArrangeAsync(deleteAfterTranscription: true);
        TranscriptionSucceeds();

        await worker.ProcessJobAsync(new TranscriptionJobRequest(jobId, file), CancellationToken.None);

        File.Exists(file).Should().BeFalse();
        (await GetJobAsync(jobId)).Status.Should().Be(AudioJobStatus.Completed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProcessJob_WhenDeleteAfterTranscriptionDisabled_KeepsUpload(bool transcriptionFails)
    {
        var (worker, jobId, file) = await ArrangeAsync(deleteAfterTranscription: false);
        if (transcriptionFails)
            TranscriptionThrows(new InvalidOperationException("boom"));
        else
            TranscriptionSucceeds();

        await worker.ProcessJobAsync(new TranscriptionJobRequest(jobId, file), CancellationToken.None);

        File.Exists(file).Should().BeTrue();
    }

    [Fact]
    public async Task ProcessJob_WhenCleanupFails_LogsWarningAndKeepsJobStatus()
    {
        var store = new Mock<ITempFileStore>();
        store.Setup(s => s.CleanupAfterProcessing(It.IsAny<string>()))
            .Throws(new IOException("file locked"));
        var (worker, jobId, file) = await ArrangeAsync(deleteAfterTranscription: true, store: store.Object);
        TranscriptionSucceeds();

        var exception = await Record.ExceptionAsync(() =>
            worker.ProcessJobAsync(new TranscriptionJobRequest(jobId, file), CancellationToken.None));

        exception.Should().BeNull();
        (await GetJobAsync(jobId)).Status.Should().Be(AudioJobStatus.Completed);
        _logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<IOException>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task ProcessJob_WhenCancelledByShutdown_KeepsUploadForRecovery()
    {
        var (worker, jobId, file) = await ArrangeAsync(deleteAfterTranscription: true);
        using var shutdown = new CancellationTokenSource();
        _transcription
            .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((_, ct) =>
            {
                shutdown.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(new TranscriptionResult("unreachable", null, null));
            });

        await Record.ExceptionAsync(() =>
            worker.ProcessJobAsync(new TranscriptionJobRequest(jobId, file), shutdown.Token));

        File.Exists(file).Should().BeTrue();
    }

    [Fact]
    public async Task ProcessJob_WhenJobNotFound_DeletesUpload()
    {
        var (worker, _, file) = await ArrangeAsync(deleteAfterTranscription: true);

        await worker.ProcessJobAsync(new TranscriptionJobRequest(Guid.NewGuid(), file), CancellationToken.None);

        File.Exists(file).Should().BeFalse();
        _transcription.Verify(
            t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private async Task<(TranscriptionWorker Worker, Guid JobId, string File)> ArrangeAsync(
        bool deleteAfterTranscription, ITempFileStore? store = null)
    {
        var options = Options.Create(new UploadOptions
        {
            TempStoragePath = _dir.Path,
            DeleteAfterTranscription = deleteAfterTranscription
        });

        var clientProxy = new Mock<IClientProxy>();
        var hubClients = new Mock<IHubClients>();
        hubClients.Setup(c => c.All).Returns(clientProxy.Object);
        var hubContext = new Mock<IHubContext<TranscriptionHub>>();
        hubContext.Setup(h => h.Clients).Returns(hubClients.Object);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddSingleton(options);
        services.AddSingleton(store ?? new TempFileStore(options));
        services.AddSingleton(_transcription.Object);
        services.AddSingleton(hubContext.Object);
        _provider = services.BuildServiceProvider();

        var jobId = Guid.NewGuid();
        var file = _dir.CreateFile($"{jobId}.mp3");

        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AudioJobs.Add(new AudioJob { Id = jobId, FileName = "meeting.mp3", ContentType = "audio/mpeg" });
            await db.SaveChangesAsync();
        }

        var worker = new TranscriptionWorker(
            new TranscriptionQueue(),
            _provider.GetRequiredService<IServiceScopeFactory>(),
            _logger.Object);

        return (worker, jobId, file);
    }

    private void TranscriptionSucceeds() =>
        _transcription
            .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranscriptionResult("Hallo Welt", "de", 1.5));

    private void TranscriptionThrows(Exception exception) =>
        _transcription
            .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

    private async Task<AudioJob> GetJobAsync(Guid jobId)
    {
        using var scope = _provider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AudioJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
    }

    public void Dispose()
    {
        _provider?.Dispose();
        _dir.Dispose();
    }
}
