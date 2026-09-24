using AudioTranscription.Api.Storage;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Api.BackgroundServices;

/// <summary>
/// The transcription queue lives in memory, so queued jobs are lost when the API stops.
/// At startup this service re-enqueues all open jobs from the database (the source of truth):
/// interrupted jobs (Processing) are reset to Pending, jobs whose upload is gone are marked Failed.
/// </summary>
public class JobRecoveryService(
    IServiceScopeFactory scopeFactory,
    TranscriptionQueue queue,
    ITempFileStore tempFileStore,
    ILogger<JobRecoveryService> logger) : IHostedService
{
    public const string SourceFileMissingMessage = "Quelldatei nach Neustart nicht mehr vorhanden.";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var openJobs = await dbContext.AudioJobs
            .Where(j => j.Status == AudioJobStatus.Pending || j.Status == AudioJobStatus.Processing)
            .OrderBy(j => j.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        if (openJobs.Count == 0)
            return;

        var toEnqueue = new List<TranscriptionJobRequest>();
        foreach (var job in openJobs)
        {
            var filePath = tempFileStore.GetUploadPath(job.Id, job.FileName);
            if (!File.Exists(filePath))
            {
                logger.LogWarning("Upload of job {JobId} is missing, marking job as failed", job.Id);
                job.Status = AudioJobStatus.Failed;
                job.ErrorMessage = SourceFileMissingMessage;
                job.CompletedAtUtc = DateTime.UtcNow;
                continue;
            }

            job.Status = AudioJobStatus.Pending;
            toEnqueue.Add(new TranscriptionJobRequest(job.Id, filePath));
        }

        // Persist state changes before the worker can pick up any of the jobs
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var request in toEnqueue)
            await queue.EnqueueAsync(request, cancellationToken);

        logger.LogInformation(
            "Recovered {Enqueued} open job(s) after restart, {Failed} failed due to missing upload",
            toEnqueue.Count, openJobs.Count - toEnqueue.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
