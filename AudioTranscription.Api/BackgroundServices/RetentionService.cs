using AudioTranscription.Api.Access;
using AudioTranscription.Api.Configuration;
using AudioTranscription.Api.Storage;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudioTranscription.Api.BackgroundServices;

/// <summary>
/// Deletes data that outlived the configured retention period (S30): whole jobs after
/// <see cref="RetentionOptions.TranscriptDays"/> and, separately, only the uploaded audio after
/// <see cref="RetentionOptions.AudioDays"/>. Open jobs and jobs locked by their owner are never touched.
/// Runs once at startup and then every <see cref="RetentionOptions.RunIntervalHours"/>; a run that fails
/// for one job logs it and carries on, the job is retried at the next run.
/// </summary>
public class RetentionService(
    IServiceScopeFactory scopeFactory,
    ITempFileStore tempFileStore,
    IOptions<RetentionOptions> options,
    TimeProvider timeProvider,
    ILogger<RetentionService> logger) : BackgroundService
{
    private static readonly AudioJobStatus[] FinishedStatuses =
        [AudioJobStatus.Completed, AudioJobStatus.Failed, AudioJobStatus.Cancelled];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (settings.TranscriptDays <= 0 && settings.AudioDays <= 0)
        {
            logger.LogInformation("Retention is off (Retention:TranscriptDays and Retention:AudioDays are 0)");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(Math.Max(1, settings.RunIntervalHours)), timeProvider);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Retention run failed, trying again at the next interval");
            }
        }
        while (await WaitForNextRunAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitForNextRunAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false; // shutting down
        }
    }

    /// <summary>One retention pass. Public so tests can run it with a simulated clock.</summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (settings.TranscriptDays > 0)
            await DeleteExpiredJobsAsync(now.AddDays(-settings.TranscriptDays), cancellationToken);
        if (settings.AudioDays > 0)
            await DeleteExpiredUploadsAsync(now.AddDays(-settings.AudioDays), cancellationToken);
    }

    private async Task DeleteExpiredJobsAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
    {
        var expired = await FindExpiredAsync(cutoffUtc, cancellationToken);

        foreach (var (id, fileName) in expired)
        {
            try
            {
                // A fresh scope per job: a failure must not leave a broken change tracker for the next one
                using var scope = scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var notifier = scope.ServiceProvider.GetRequiredService<IJobNotifier>();

                var job = await dbContext.AudioJobs.FindAsync([id], cancellationToken);
                if (job is null || job.RetentionLocked)
                    continue; // deleted or locked since the list was read

                var recipients = await notifier.GetRecipientsAsync(id, cancellationToken);
                dbContext.AudioJobs.Remove(job);
                await dbContext.SaveChangesAsync(cancellationToken);
                tempFileStore.DeleteUpload(tempFileStore.GetUploadPath(id, fileName));
                await notifier.SendAsync(recipients, "JobDeleted", id, cancellationToken);

                // Audit trail: only the id, never the file name or any content
                logger.LogInformation("Retention deleted job {JobId}", id);
            }
            catch (Exception ex) when (ex is DbUpdateException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                logger.LogWarning(ex, "Retention could not delete job {JobId}, trying again at the next run", id);
            }
        }
    }

    private async Task DeleteExpiredUploadsAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
    {
        var expired = await FindExpiredAsync(cutoffUtc, cancellationToken);

        foreach (var (id, fileName) in expired)
        {
            var path = tempFileStore.GetUploadPath(id, fileName);
            if (!File.Exists(path))
                continue;

            try
            {
                tempFileStore.DeleteUpload(path);
                logger.LogInformation("Retention deleted the upload of job {JobId}", id);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Retention could not delete the upload of job {JobId}, trying again at the next run", id);
            }
        }
    }

    /// <summary>Finished, unlocked jobs whose completion (or creation) time is older than the cutoff.</summary>
    private async Task<List<(Guid Id, string FileName)>> FindExpiredAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rows = await dbContext.AudioJobs
            .AsNoTracking()
            .Where(j => !j.RetentionLocked
                && FinishedStatuses.Contains(j.Status)
                && (j.CompletedAtUtc ?? j.CreatedAtUtc) < cutoffUtc)
            .Select(j => new { j.Id, j.FileName })
            .ToListAsync(cancellationToken);
        return rows.Select(r => (r.Id, r.FileName)).ToList();
    }
}
