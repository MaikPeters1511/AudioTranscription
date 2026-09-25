using AudioTranscription.Api.Dtos;
using AudioTranscription.Api.Hubs;
using AudioTranscription.Api.Storage;
using AudioTranscription.Domain.Diarization;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using AudioTranscription.Infrastructure.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Api.BackgroundServices;

/// <summary>
/// Long-running background service that processes transcription jobs from the queue.
/// Reads jobs sequentially, transcribes audio with Whisper.net, updates the database,
/// and pushes status changes via SignalR.
/// </summary>
public class TranscriptionWorker : BackgroundService
{
    private readonly TranscriptionQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly JobCancellationRegistry _cancellations;
    private readonly JobProgressStore _progressStore;
    private readonly ILogger<TranscriptionWorker> _logger;

    public TranscriptionWorker(
        TranscriptionQueue queue,
        IServiceScopeFactory scopeFactory,
        JobCancellationRegistry cancellations,
        JobProgressStore progressStore,
        ILogger<TranscriptionWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _cancellations = cancellations;
        _progressStore = progressStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TranscriptionWorker started, waiting for jobs...");

        await foreach (var request in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessJobAsync(request, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("TranscriptionWorker stopping due to cancellation");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error processing job {JobId}", request.JobId);
            }
        }

        _logger.LogInformation("TranscriptionWorker stopped");
    }

    internal async Task ProcessJobAsync(TranscriptionJobRequest request, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Processing transcription job {JobId} from file {FilePath}",
            request.JobId, request.FilePath);

        using var scope = _scopeFactory.CreateScope();
        var tempFileStore = scope.ServiceProvider.GetRequiredService<ITempFileStore>();

        // Fires on user cancellation (API) and on application shutdown
        using var jobCancellation = _cancellations.Register(request.JobId, stoppingToken);
        AudioJobStatus? outcome = AudioJobStatus.Failed; // unexpected errors keep the upload
        try
        {
            outcome = await TranscribeJobAsync(scope.ServiceProvider, request, jobCancellation.Token, stoppingToken);
        }
        finally
        {
            _cancellations.Unregister(request.JobId);
            _progressStore.Remove(request.JobId);

            // On shutdown the upload is kept so the job can be recovered after a restart
            if (!stoppingToken.IsCancellationRequested)
                CleanupUpload(tempFileStore, request.FilePath, outcome);
        }
    }

    /// <returns>The job's final status, or null if the job does not exist (anymore).</returns>
    private async Task<AudioJobStatus?> TranscribeJobAsync(
        IServiceProvider services, TranscriptionJobRequest request,
        CancellationToken cancellationToken, CancellationToken stoppingToken)
    {
        var dbContext = services.GetRequiredService<AppDbContext>();
        var transcriptionService = services.GetRequiredService<ITranscriptionService>();
        var hubContext = services.GetRequiredService<IHubContext<TranscriptionHub>>();

        var job = await dbContext.AudioJobs.FindAsync([request.JobId], cancellationToken);
        if (job is null)
        {
            _logger.LogWarning("Job {JobId} not found in database, skipping", request.JobId);
            return null;
        }

        // A job can be enqueued twice (e.g. by startup recovery and a concurrent upload)
        if (job.Status != AudioJobStatus.Pending)
        {
            _logger.LogInformation("Job {JobId} is {Status}, not Pending; skipping", request.JobId, job.Status);
            return job.Status;
        }

        // Update status to Processing
        job.Status = AudioJobStatus.Processing;
        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyStatusChanged(hubContext, job);

        try
        {
            // Run transcription with the settings chosen at upload (also after a restart or retry)
            var settings = new TranscriptionSettings(job.Model, job.RequestedLanguage);
            var progress = CreateProgress(services, hubContext, job.Id);
            var result = await transcriptionService.TranscribeAsync(request.FilePath, settings, progress, cancellationToken);

            // Update job with results
            job.Status = AudioJobStatus.Completed;
            job.RawTranscript = result.Text;
            job.Language = result.DetectedLanguage;
            job.DurationSeconds = result.DurationSeconds;
            job.CompletedAtUtc = DateTime.UtcNow;
            var segments = result.Segments.Select((segment, index) => new TranscriptSegment
            {
                AudioJobId = job.Id,
                Index = index,
                StartMs = (long)Math.Round(segment.Start.TotalMilliseconds),
                EndMs = (long)Math.Round(segment.End.TotalMilliseconds),
                Text = segment.Text
            }).ToList();

            if (job.DiarizationRequested)
                await ApplyDiarizationAsync(services, request.FilePath, segments, dbContext, job.Id, cancellationToken);

            // Saved together with the Completed status, so only completed jobs have segments
            dbContext.TranscriptSegments.AddRange(segments);

            await dbContext.SaveChangesAsync(cancellationToken);
            await NotifyStatusChanged(hubContext, job);

            _logger.LogInformation(
                "Job {JobId} completed: {CharCount} chars, language={Language}, duration={Duration:F1}s",
                request.JobId, result.Text.Length, result.DetectedLanguage, result.DurationSeconds);

            // Automatic cleanup (S04); other modes are generated on demand (S10). Never fails the job itself.
            if (services.GetService<IVariantGenerator>() is not null)
                await EnqueueAutomaticCleanupVariant(services, dbContext, job.Id, cancellationToken);

            return AudioJobStatus.Completed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Job {JobId} was cancelled by a user", request.JobId);
            return await MarkCancelledAsync(dbContext, hubContext, job, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transcription failed for job {JobId}", request.JobId);

            job.Status = AudioJobStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.CompletedAtUtc = DateTime.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken);
            await NotifyStatusChanged(hubContext, job);
            return AudioJobStatus.Failed;
        }
    }

    /// <summary>
    /// Diarizes the original upload and assigns each transcript segment its speaker by largest time
    /// overlap (S11-T3). Never fails the job: without a configured <see cref="IDiarizationService"/>,
    /// or if diarization itself throws, segments are simply kept without a speaker.
    /// </summary>
    private async Task ApplyDiarizationAsync(
        IServiceProvider services, string audioFilePath, List<TranscriptSegment> segments,
        AppDbContext dbContext, Guid jobId, CancellationToken cancellationToken)
    {
        var diarizationService = services.GetService<IDiarizationService>();
        if (diarizationService is null)
        {
            _logger.LogWarning("Job {JobId} requested diarization, but it is not configured; skipping", jobId);
            return;
        }

        try
        {
            var speakerIntervals = await diarizationService.DiarizeAsync(audioFilePath, expectedSpeakerCount: null, cancellationToken);
            if (speakerIntervals.Count == 0)
                return;

            foreach (var segment in segments)
                segment.SpeakerIndex = SpeakerOverlapAssigner.Assign(segment.StartMs, segment.EndMs, speakerIntervals);

            foreach (var speakerIndex in speakerIntervals.Select(i => i.SpeakerIndex).Distinct().OrderBy(i => i))
                dbContext.JobSpeakers.Add(new JobSpeaker { AudioJobId = jobId, Index = speakerIndex });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Speaker diarization failed for job {JobId}; segments are kept without a speaker", jobId);
        }
    }

    private async Task EnqueueAutomaticCleanupVariant(
        IServiceProvider services, AppDbContext dbContext, Guid jobId, CancellationToken cancellationToken)
    {
        try
        {
            var variant = new TranscriptVariant { AudioJobId = jobId, Mode = PostProcessingMode.Cleanup };
            dbContext.TranscriptVariants.Add(variant);
            await dbContext.SaveChangesAsync(cancellationToken);
            await services.GetRequiredService<VariantQueue>().EnqueueAsync(new VariantJobRequest(variant.Id), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to queue the automatic cleanup variant for job {JobId}", jobId);
        }
    }

    private async Task<AudioJobStatus?> MarkCancelledAsync(
        AppDbContext dbContext, IHubContext<TranscriptionHub> hubContext, AudioJob job, CancellationToken stoppingToken)
    {
        job.Status = AudioJobStatus.Cancelled;
        job.ErrorMessage = null;
        job.CompletedAtUtc = DateTime.UtcNow;
        try
        {
            await dbContext.SaveChangesAsync(stoppingToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The job was deleted while it was being cancelled (DELETE cancels running jobs first)
            _logger.LogInformation("Job {JobId} was deleted while being cancelled", job.Id);
            return null;
        }

        await NotifyStatusChanged(hubContext, job);
        return AudioJobStatus.Cancelled;
    }

    /// <summary>
    /// Keeps the latest value for clients that load the job later and pushes throttled "JobProgress" events.
    /// Called on Whisper's thread, so sending is not awaited.
    /// </summary>
    private ThrottledProgress CreateProgress(IServiceProvider services, IHubContext<TranscriptionHub> hubContext, Guid jobId)
    {
        var timeProvider = services.GetService<TimeProvider>() ?? TimeProvider.System;
        return new ThrottledProgress(timeProvider, percent =>
        {
            _progressStore.Set(jobId, percent);
            hubContext.Clients.All.SendAsync("JobProgress", new JobProgressDto(jobId, percent))
                .ContinueWith(
                    t => _logger.LogWarning(t.Exception, "Failed to push progress of job {JobId}", jobId),
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        });
    }

    private void CleanupUpload(ITempFileStore tempFileStore, string filePath, AudioJobStatus? outcome)
    {
        try
        {
            tempFileStore.CleanupAfterProcessing(filePath, outcome);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to delete temp file {FilePath}", filePath);
        }
    }

    private static async Task NotifyStatusChanged(
        IHubContext<TranscriptionHub> hubContext,
        AudioJob job)
    {
        var dto = new AudioJobListDto(
            job.Id, job.FileName, job.FileSizeBytes,
            job.Status, job.Language, job.DurationSeconds,
            job.CreatedAtUtc, job.CompletedAtUtc);

        await hubContext.Clients.All.SendAsync("JobStatusChanged", dto);
    }
}
