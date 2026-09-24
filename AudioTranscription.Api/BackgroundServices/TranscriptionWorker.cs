using AudioTranscription.Api.Dtos;
using AudioTranscription.Api.Hubs;
using AudioTranscription.Api.Storage;
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
    private readonly ILogger<TranscriptionWorker> _logger;

    public TranscriptionWorker(
        TranscriptionQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<TranscriptionWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
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

    internal async Task ProcessJobAsync(TranscriptionJobRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing transcription job {JobId} from file {FilePath}",
            request.JobId, request.FilePath);

        using var scope = _scopeFactory.CreateScope();
        var tempFileStore = scope.ServiceProvider.GetRequiredService<ITempFileStore>();

        try
        {
            await TranscribeJobAsync(scope.ServiceProvider, request, cancellationToken);
        }
        finally
        {
            // On shutdown the upload is kept so the job can be recovered after a restart
            if (!cancellationToken.IsCancellationRequested)
                CleanupUpload(tempFileStore, request.FilePath);
        }
    }

    private async Task TranscribeJobAsync(
        IServiceProvider services, TranscriptionJobRequest request, CancellationToken cancellationToken)
    {
        var dbContext = services.GetRequiredService<AppDbContext>();
        var transcriptionService = services.GetRequiredService<ITranscriptionService>();
        var hubContext = services.GetRequiredService<IHubContext<TranscriptionHub>>();
        var postProcessor = services.GetService<ITranscriptPostProcessor>();

        var job = await dbContext.AudioJobs.FindAsync([request.JobId], cancellationToken);
        if (job is null)
        {
            _logger.LogWarning("Job {JobId} not found in database, skipping", request.JobId);
            return;
        }

        // A job can be enqueued twice (e.g. by startup recovery and a concurrent upload)
        if (job.Status != AudioJobStatus.Pending)
        {
            _logger.LogInformation("Job {JobId} is {Status}, not Pending; skipping", request.JobId, job.Status);
            return;
        }

        // Update status to Processing
        job.Status = AudioJobStatus.Processing;
        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyStatusChanged(hubContext, job);

        try
        {
            // Run transcription
            var result = await transcriptionService.TranscribeAsync(request.FilePath, cancellationToken);

            // Optional post-processing
            var finalTranscript = result.Text;
            if (postProcessor is not null)
            {
                finalTranscript = await postProcessor.ProcessAsync(result.Text, cancellationToken);
            }

            // Update job with results
            job.Status = AudioJobStatus.Completed;
            job.TranscriptText = finalTranscript;
            job.Language = result.DetectedLanguage;
            job.DurationSeconds = result.DurationSeconds;
            job.CompletedAtUtc = DateTime.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken);
            await NotifyStatusChanged(hubContext, job);

            _logger.LogInformation(
                "Job {JobId} completed: {CharCount} chars, language={Language}, duration={Duration:F1}s",
                request.JobId, finalTranscript.Length, result.DetectedLanguage, result.DurationSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transcription failed for job {JobId}", request.JobId);

            job.Status = AudioJobStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.CompletedAtUtc = DateTime.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken);
            await NotifyStatusChanged(hubContext, job);
        }
    }

    private void CleanupUpload(ITempFileStore tempFileStore, string filePath)
    {
        try
        {
            tempFileStore.CleanupAfterProcessing(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to delete temp file {FilePath}", filePath);
        }
    }

    private static async Task NotifyStatusChanged(
        IHubContext<TranscriptionHub> hubContext,
        Domain.Entities.AudioJob job)
    {
        var dto = new AudioJobListDto(
            job.Id, job.FileName, job.FileSizeBytes,
            job.Status, job.Language, job.DurationSeconds,
            job.CreatedAtUtc, job.CompletedAtUtc);

        await hubContext.Clients.All.SendAsync("JobStatusChanged", dto);
    }
}
