using AudioTranscription.Api.BackgroundServices;
using AudioTranscription.Api.Configuration;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Api.Hubs;
using AudioTranscription.Api.Storage;
using AudioTranscription.Domain.Diarization;
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
    private readonly JobCancellationRegistry _registry = new();
    private readonly JobProgressStore _progressStore = new();
    private readonly Mock<IClientProxy> _clientProxy = new();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private ServiceProvider? _provider;

    [Fact]
    public async Task ProcessJob_WhenTranscriptionFails_KeepsUploadForRetry()
    {
        var (worker, jobId, file) = await ArrangeAsync(deleteAfterTranscription: true);
        TranscriptionThrows(new InvalidOperationException("ffmpeg missing"));

        await worker.ProcessJobAsync(new TranscriptionJobRequest(jobId, file), CancellationToken.None);

        File.Exists(file).Should().BeTrue();
        (await GetJobAsync(jobId)).Status.Should().Be(AudioJobStatus.Failed);
    }

    [Fact]
    public async Task ProcessJob_WhenCancelledByUser_SetsCancelledAndKeepsUpload()
    {
        var (worker, jobId, file) = await ArrangeAsync(deleteAfterTranscription: true);
        _transcription
            .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TranscriptionSettings>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()))
            .Returns<string, TranscriptionSettings, IProgress<int>?, CancellationToken>((_, _, _, ct) =>
            {
                _registry.Cancel(jobId).Should().BeTrue("the job is registered while it runs");
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(new TranscriptionResult("unreachable", null, null));
            });

        await worker.ProcessJobAsync(new TranscriptionJobRequest(jobId, file), CancellationToken.None);

        var job = await GetJobAsync(jobId);
        job.Status.Should().Be(AudioJobStatus.Cancelled);
        job.ErrorMessage.Should().BeNull();
        job.CompletedAtUtc.Should().NotBeNull();
        File.Exists(file).Should().BeTrue("a cancelled job can be retried");
        _registry.Cancel(jobId).Should().BeFalse("the job is unregistered once processing ended");
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

    [Fact]
    public async Task ProcessJob_WhenDeleteAfterTranscriptionDisabled_KeepsUpload()
    {
        var (worker, jobId, file) = await ArrangeAsync(deleteAfterTranscription: false);
        TranscriptionSucceeds();

        await worker.ProcessJobAsync(new TranscriptionJobRequest(jobId, file), CancellationToken.None);

        File.Exists(file).Should().BeTrue();
    }

    [Fact]
    public async Task ProcessJob_WhenJobIsDeletedWhileCancelling_EndsQuietlyAndRemovesUpload()
    {
        var (worker, jobId, file) = await ArrangeAsync(deleteAfterTranscription: true);
        _transcription
            .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TranscriptionSettings>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()))
            .Returns<string, TranscriptionSettings, IProgress<int>?, CancellationToken>(async (_, _, _, ct) =>
            {
                // What DELETE does: cancel the running job, then remove it
                _registry.Cancel(jobId);
                using (var scope = _provider!.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    db.AudioJobs.Remove(await db.AudioJobs.SingleAsync(j => j.Id == jobId));
                    await db.SaveChangesAsync();
                }
                ct.ThrowIfCancellationRequested();
                return new TranscriptionResult("unreachable", null, null);
            });

        var exception = await Record.ExceptionAsync(() =>
            worker.ProcessJobAsync(new TranscriptionJobRequest(jobId, file), CancellationToken.None));

        exception.Should().BeNull();
        File.Exists(file).Should().BeFalse();
    }

    [Fact]
    public async Task ProcessJob_WhenCleanupFails_LogsWarningAndKeepsJobStatus()
    {
        var store = new Mock<ITempFileStore>();
        store.Setup(s => s.CleanupAfterProcessing(It.IsAny<string>(), It.IsAny<AudioJobStatus?>()))
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
            .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TranscriptionSettings>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()))
            .Returns<string, TranscriptionSettings, IProgress<int>?, CancellationToken>((_, _, _, ct) =>
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
            t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TranscriptionSettings>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(AudioJobStatus.Processing)]
    [InlineData(AudioJobStatus.Completed)]
    [InlineData(AudioJobStatus.Failed)]
    public async Task ProcessJob_WhenJobIsNoLongerPending_SkipsTranscription(AudioJobStatus status)
    {
        var (worker, jobId, file) = await ArrangeAsync(deleteAfterTranscription: true, status: status);
        TranscriptionSucceeds();

        await worker.ProcessJobAsync(new TranscriptionJobRequest(jobId, file), CancellationToken.None);

        _transcription.Verify(
            t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TranscriptionSettings>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()), Times.Never);
        (await GetJobAsync(jobId)).Status.Should().Be(status);
    }

    [Fact]
    public async Task ProcessJob_PassesModelAndLanguageOfTheJobToTranscription()
    {
        var (worker, jobId, file) = await ArrangeAsync(
            deleteAfterTranscription: true, model: "Small", requestedLanguage: "de");
        TranscriptionSucceeds();

        await worker.ProcessJobAsync(new TranscriptionJobRequest(jobId, file), CancellationToken.None);

        _transcription.Verify(t => t.TranscribeAsync(
            file, new TranscriptionSettings("Small", "de"), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessJob_WithoutRequestedLanguage_UsesLanguageDetection()
    {
        var (worker, jobId, file) = await ArrangeAsync(deleteAfterTranscription: true, model: "Base");
        TranscriptionSucceeds();

        await worker.ProcessJobAsync(new TranscriptionJobRequest(jobId, file), CancellationToken.None);

        _transcription.Verify(t => t.TranscribeAsync(
            file, new TranscriptionSettings("Base", null), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()), Times.Once);
        (await GetJobAsync(jobId)).Language.Should().Be("de", "the detected language is stored");
    }

    [Fact]
    public async Task ProcessJob_WhenTranscriptionSucceeds_StoresSegmentsInMilliseconds()
    {
        var (worker, jobId, file) = await ArrangeAsync(deleteAfterTranscription: true);
        _transcription
            .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TranscriptionSettings>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranscriptionResult("Hallo Welt", "de", 2.5)
            {
                Segments =
                [
                    new SegmentResult(TimeSpan.Zero, TimeSpan.FromMilliseconds(1234.6), "Hallo"),
                    new SegmentResult(TimeSpan.FromMilliseconds(1234.6), TimeSpan.FromSeconds(2.5), "Welt"),
                ]
            });

        await worker.ProcessJobAsync(new TranscriptionJobRequest(jobId, file), CancellationToken.None);

        using var scope = _provider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var segments = await db.TranscriptSegments.Where(s => s.AudioJobId == jobId).OrderBy(s => s.Index).ToListAsync();
        segments.Select(s => (s.Index, s.StartMs, s.EndMs, s.Text)).Should().Equal(
            (0, 0L, 1235L, "Hallo"),
            (1, 1235L, 2500L, "Welt"));
    }

    [Fact]
    public async Task ProcessJob_WhenTranscriptionFails_StoresNoSegments()
    {
        var (worker, jobId, file) = await ArrangeAsync(deleteAfterTranscription: true);
        TranscriptionThrows(new InvalidOperationException("ffmpeg missing"));

        await worker.ProcessJobAsync(new TranscriptionJobRequest(jobId, file), CancellationToken.None);

        using var scope = _provider!.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().TranscriptSegments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProcessJob_PushesProgressAndKeepsTheLatestValueWhileRunning()
    {
        var (worker, jobId, file) = await ArrangeAsync(deleteAfterTranscription: true);
        int? storedWhileRunning = null;
        _transcription
            .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TranscriptionSettings>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()))
            .Returns<string, TranscriptionSettings, IProgress<int>?, CancellationToken>((_, _, progress, _) =>
            {
                progress!.Report(42);
                storedWhileRunning = _progressStore.Get(jobId);
                progress.Report(100);
                return Task.FromResult(new TranscriptionResult("Hallo", "de", 1));
            });

        await worker.ProcessJobAsync(new TranscriptionJobRequest(jobId, file), CancellationToken.None);

        storedWhileRunning.Should().Be(42, "clients that connect later can be given the current value");
        _progressStore.Get(jobId).Should().BeNull("the value is dropped once the job has ended");
        foreach (var percent in new[] { 42, 100 })
        {
            _clientProxy.Verify(c => c.SendCoreAsync(
                "JobProgress",
                It.Is<object?[]>(args => args.Length == 1 && args[0]!.Equals(new JobProgressDto(jobId, percent))),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task ProcessJob_WhenTranscriptionFails_DropsTheProgressValue()
    {
        var (worker, jobId, file) = await ArrangeAsync(deleteAfterTranscription: true);
        _transcription
            .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TranscriptionSettings>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()))
            .Returns<string, TranscriptionSettings, IProgress<int>?, CancellationToken>((_, _, progress, _) =>
            {
                progress!.Report(30);
                throw new InvalidOperationException("ffmpeg missing");
            });

        await worker.ProcessJobAsync(new TranscriptionJobRequest(jobId, file), CancellationToken.None);

        _progressStore.Get(jobId).Should().BeNull();
    }

    [Fact]
    public async Task ProcessJob_WhenDiarizationRequestedAndConfigured_AssignsSpeakersAndStoresJobSpeakers()
    {
        var diarization = new Mock<IDiarizationService>();
        diarization
            .Setup(d => d.DiarizeAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SpeakerInterval> { new(0, 1_500, 0), new(1_500, 3_000, 1) });
        var (worker, jobId, file) = await ArrangeAsync(
            deleteAfterTranscription: true, diarizationService: diarization.Object, diarizationRequested: true);
        _transcription
            .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TranscriptionSettings>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranscriptionResult("Hallo Welt", "de", 3)
            {
                Segments =
                [
                    new SegmentResult(TimeSpan.Zero, TimeSpan.FromMilliseconds(1_000), "Hallo"),
                    new SegmentResult(TimeSpan.FromMilliseconds(2_000), TimeSpan.FromMilliseconds(3_000), "Welt"),
                ]
            });

        await worker.ProcessJobAsync(new TranscriptionJobRequest(jobId, file), CancellationToken.None);

        using var scope = _provider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var segments = await db.TranscriptSegments.Where(s => s.AudioJobId == jobId).OrderBy(s => s.Index).ToListAsync();
        segments.Select(s => s.SpeakerIndex).Should().Equal(0, 1);
        var speakers = await db.JobSpeakers.Where(s => s.AudioJobId == jobId).OrderBy(s => s.Index).ToListAsync();
        speakers.Select(s => s.Index).Should().Equal(0, 1);
        speakers.Should().OnlyContain(s => s.DisplayName == null);
    }

    [Fact]
    public async Task ProcessJob_WhenDiarizationNotRequested_NeverCallsTheDiarizationService()
    {
        var diarization = new Mock<IDiarizationService>();
        var (worker, jobId, file) = await ArrangeAsync(
            deleteAfterTranscription: true, diarizationService: diarization.Object, diarizationRequested: false);
        TranscriptionSucceeds();

        await worker.ProcessJobAsync(new TranscriptionJobRequest(jobId, file), CancellationToken.None);

        diarization.Verify(d => d.DiarizeAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessJob_WhenDiarizationRequestedButNotConfigured_StillCompletesTheJob()
    {
        var (worker, jobId, file) = await ArrangeAsync(deleteAfterTranscription: true, diarizationRequested: true);
        TranscriptionSucceeds();

        await worker.ProcessJobAsync(new TranscriptionJobRequest(jobId, file), CancellationToken.None);

        (await GetJobAsync(jobId)).Status.Should().Be(AudioJobStatus.Completed);
    }

    [Fact]
    public async Task ProcessJob_WhenDiarizationThrows_JobStillCompletesWithoutSpeakers()
    {
        var diarization = new Mock<IDiarizationService>();
        diarization
            .Setup(d => d.DiarizeAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Modelle fehlen"));
        var (worker, jobId, file) = await ArrangeAsync(
            deleteAfterTranscription: true, diarizationService: diarization.Object, diarizationRequested: true);
        TranscriptionSucceeds();

        await worker.ProcessJobAsync(new TranscriptionJobRequest(jobId, file), CancellationToken.None);

        var job = await GetJobAsync(jobId);
        job.Status.Should().Be(AudioJobStatus.Completed);
        using var scope = _provider!.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().JobSpeakers.CountAsync(s => s.AudioJobId == jobId)).Should().Be(0);
    }

    [Fact]
    public async Task ProcessJob_WithoutVariantGenerator_CreatesNoAutomaticVariant()
    {
        var (worker, jobId, file) = await ArrangeAsync(deleteAfterTranscription: true);
        TranscriptionSucceeds();

        await worker.ProcessJobAsync(new TranscriptionJobRequest(jobId, file), CancellationToken.None);

        var job = await GetJobAsync(jobId);
        job.RawTranscript.Should().Be("Hallo Welt");
        using var scope = _provider!.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().TranscriptVariants.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProcessJob_WithVariantGenerator_QueuesAnAutomaticCleanupVariant()
    {
        var variantQueue = new VariantQueue();
        var (worker, jobId, file) = await ArrangeAsync(deleteAfterTranscription: true, variantGenerator: Mock.Of<IVariantGenerator>(), variantQueue: variantQueue);
        TranscriptionSucceeds();

        await worker.ProcessJobAsync(new TranscriptionJobRequest(jobId, file), CancellationToken.None);

        using var scope = _provider!.CreateScope();
        var variants = await scope.ServiceProvider.GetRequiredService<AppDbContext>().TranscriptVariants.ToListAsync();
        variants.Should().ContainSingle(v => v.AudioJobId == jobId && v.Mode == PostProcessingMode.Cleanup && v.Status == VariantStatus.Pending);
        variantQueue.TryRead(out var request).Should().BeTrue();
        request!.VariantId.Should().Be(variants.Single().Id);
    }

    private async Task<(TranscriptionWorker Worker, Guid JobId, string File)> ArrangeAsync(
        bool deleteAfterTranscription,
        ITempFileStore? store = null,
        AudioJobStatus status = AudioJobStatus.Pending,
        IVariantGenerator? variantGenerator = null,
        VariantQueue? variantQueue = null,
        IDiarizationService? diarizationService = null,
        bool diarizationRequested = false,
        string model = "Base",
        string? requestedLanguage = null)
    {
        var options = Options.Create(new UploadOptions
        {
            TempStoragePath = _dir.Path,
            DeleteAfterTranscription = deleteAfterTranscription
        });

        var hubClients = new Mock<IHubClients>();
        hubClients.Setup(c => c.All).Returns(_clientProxy.Object);
        var hubContext = new Mock<IHubContext<TranscriptionHub>>();
        hubContext.Setup(h => h.Clients).Returns(hubClients.Object);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddSingleton(options);
        services.AddSingleton(store ?? new TempFileStore(options));
        services.AddSingleton(_transcription.Object);
        services.AddSingleton(hubContext.Object);
        services.AddSingleton(variantQueue ?? new VariantQueue());
        if (variantGenerator is not null)
            services.AddSingleton(variantGenerator);
        if (diarizationService is not null)
            services.AddSingleton(diarizationService);
        _provider = services.BuildServiceProvider();

        var jobId = Guid.NewGuid();
        var file = _dir.CreateFile($"{jobId}.mp3");

        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AudioJobs.Add(new AudioJob
            {
                Id = jobId, FileName = "meeting.mp3", ContentType = "audio/mpeg", Status = status,
                Model = model, RequestedLanguage = requestedLanguage, DiarizationRequested = diarizationRequested
            });
            await db.SaveChangesAsync();
        }

        var worker = new TranscriptionWorker(
            new TranscriptionQueue(),
            _provider.GetRequiredService<IServiceScopeFactory>(),
            _registry,
            _progressStore,
            _logger.Object);

        return (worker, jobId, file);
    }

    private void TranscriptionSucceeds() =>
        _transcription
            .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TranscriptionSettings>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranscriptionResult("Hallo Welt", "de", 1.5));

    private void TranscriptionThrows(Exception exception) =>
        _transcription
            .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TranscriptionSettings>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()))
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
