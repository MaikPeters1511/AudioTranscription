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
/// <see cref="RetentionOptions.AudioDays"/>. Open jobs, jobs locked by their owner and jobs without an
/// owner (nobody could lock those) are never touched. At most <see cref="RetentionOptions.MaxJobsPerRun"/>
/// jobs are deleted per run. Runs once at startup and then every <see cref="RetentionOptions.RunIntervalHours"/>;
/// a failure with one job is logged and the job is retried at the next run. Nothing here may stop the host.
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

    /// <summary>How many jobs the audio pass loads and looks at at once, so memory does not grow with the number of jobs (S30-F2).</summary>
    internal int BatchSize { get; init; } = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var settings = options.Value;
            if (settings.TranscriptDays <= 0 && settings.AudioDays <= 0)
            {
                logger.LogInformation("Retention is off (Retention:TranscriptDays and Retention:AudioDays are 0)");
                return;
            }

            logger.LogInformation(
                "Retention is on: jobs expire after {TranscriptDays} day(s), uploads after {AudioDays} day(s), checked every {Hours} hour(s), at most {Max} jobs per run",
                settings.TranscriptDays, settings.AudioDays, settings.RunIntervalHours, settings.MaxJobsPerRun);

            using var timer = new PeriodicTimer(TimeSpan.FromHours(settings.RunIntervalHours), timeProvider);
            do
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Not even the exception object: provider messages can carry paths or SQL details
                    logger.LogError("Retention run failed ({ErrorType}), trying again at the next interval", ex.GetType().Name);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            // A broken retention service must never take the API down with it
            logger.LogError("Retention stopped ({ErrorType}); no data will be deleted automatically until the next start", ex.GetType().Name);
        }
    }

    /// <summary>One retention pass. Public so tests can run it with a simulated clock.</summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (settings.TranscriptDays > 0)
            await DeleteExpiredJobsAsync(now.AddDays(-settings.TranscriptDays), settings.MaxJobsPerRun, cancellationToken);
        if (settings.AudioDays > 0)
        {
            // Jobs past the transcript limit are the transcript pass' business (their upload goes with them)
            DateTime? transcriptCutoff = settings.TranscriptDays > 0 ? now.AddDays(-settings.TranscriptDays) : null;
            await DeleteExpiredUploadsAsync(now.AddDays(-settings.AudioDays), transcriptCutoff, cancellationToken);
        }
    }

    private async Task DeleteExpiredJobsAsync(DateTime cutoffUtc, int maxJobs, CancellationToken cancellationToken)
    {
        // Only this run's share is loaded, oldest first
        var batch = await FindExpiredAsync(cutoffUtc, maxJobs, cancellationToken);
        if (batch.Count == 0)
            return;

        // Visible on purpose: switching retention on (or a clock that jumped forward) can delete a lot at once.
        // The total is only counted when the batch is full, i.e. when there may be more than this run deletes.
        var total = batch.Count < maxJobs ? batch.Count : await CountExpiredAsync(cutoffUtc, cancellationToken);
        logger.LogWarning(
            "Retention is about to delete {Count} of {Total} expired job(s) (limit {Max} per run)",
            batch.Count, total, maxJobs);

        var deleted = 0;
        foreach (var (id, fileName) in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await DeleteJobAsync(id, fileName, cutoffUtc, cancellationToken))
                    deleted++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Retention could not delete job {JobId} ({ErrorType}), trying again at the next run", id, ex.GetType().Name);
            }
        }

        logger.LogInformation("Retention deleted {Count} job(s)", deleted);
    }

    private async Task<bool> DeleteJobAsync(Guid id, string fileName, DateTime cutoffUtc, CancellationToken cancellationToken)
    {
        // A fresh scope per job: a failure must not leave a broken change tracker for the next one
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<IJobNotifier>();

        // Who to tell has to be looked up before the job and its shares are gone
        var recipients = await notifier.GetRecipientsAsync(id, cancellationToken);

        // The job may have been retried, cancelled-and-restarted or locked since the list was read: delete it
        // only if it still qualifies, and decide that in the same statement that deletes it
        if (!await TryDeleteExpiredAsync(dbContext, id, cutoffUtc, cancellationToken))
            return false;
        await Search.SemanticIndexHooks.JobDeletedAsync(scope.ServiceProvider, id, logger, cancellationToken);

        // The row is gone for good; a leftover file is not a reason to call the deletion failed
        try
        {
            tempFileStore.DeleteUpload(tempFileStore.GetUploadPath(id, fileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                "Retention deleted job {JobId} but not its upload ({ErrorType}); the orphan cleanup removes it at the next start",
                id, ex.GetType().Name);
        }

        try
        {
            await notifier.SendAsync(recipients, "JobDeleted", id, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Retention deleted job {JobId} but could not notify clients ({ErrorType})", id, ex.GetType().Name);
        }

        // Audit trail: only the id, never the file name or any content
        logger.LogInformation("Retention deleted job {JobId}", id);
        return true;
    }

    /// <summary>
    /// Deletes the job if, right now, it is finished, unlocked, owned and expired. A relational database does
    /// that in one conditional DELETE (its foreign keys remove segments, variants, speakers and shares);
    /// the in-memory test database cannot, so it re-reads the job with the same condition.
    /// </summary>
    private static async Task<bool> TryDeleteExpiredAsync(AppDbContext dbContext, Guid id, DateTime cutoffUtc, CancellationToken cancellationToken)
    {
        var expiredJob = dbContext.AudioJobs.Where(j => j.Id == id
            && j.OwnerId != null
            && !j.RetentionLocked
            && FinishedStatuses.Contains(j.Status)
            && (j.CompletedAtUtc ?? j.CreatedAtUtc) < cutoffUtc);

        if (dbContext.Database.IsRelational())
            return await expiredJob.ExecuteDeleteAsync(cancellationToken) > 0;

        var job = await expiredJob.SingleOrDefaultAsync(cancellationToken);
        if (job is null)
            return false;
        dbContext.AudioJobs.Remove(job);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task DeleteExpiredUploadsAsync(DateTime audioCutoffUtc, DateTime? transcriptCutoffUtc, CancellationToken cancellationToken)
    {
        for (var page = 0; ; page++)
        {
            var batch = await FindAudioExpiredAsync(audioCutoffUtc, transcriptCutoffUtc, page * BatchSize, BatchSize, cancellationToken);
            foreach (var (id, fileName) in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = tempFileStore.GetUploadPath(id, fileName);
                if (!File.Exists(path))
                    continue;

                try
                {
                    // Checked again right before deleting: a retry or a lock since the list was read keeps the file
                    if (!await StillExpiredAsync(id, audioCutoffUtc, cancellationToken))
                        continue;

                    tempFileStore.DeleteUpload(path);
                    logger.LogInformation("Retention deleted the upload of job {JobId}", id);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("Retention could not delete the upload of job {JobId} ({ErrorType}), trying again at the next run", id, ex.GetType().Name);
                }
            }

            if (batch.Count < BatchSize)
                return;
        }
    }

    private async Task<bool> StillExpiredAsync(Guid id, DateTime cutoffUtc, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ExpiredJobs(dbContext, cutoffUtc).AnyAsync(j => j.Id == id, cancellationToken);
    }

    /// <summary>At most <paramref name="take"/> expired jobs, oldest first, so a limit per run always works through the backlog in order.</summary>
    private async Task<List<(Guid Id, string FileName)>> FindExpiredAsync(DateTime cutoffUtc, int take, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rows = await ExpiredJobs(dbContext, cutoffUtc)
            .OrderBy(j => j.CompletedAtUtc ?? j.CreatedAtUtc)
            .ThenBy(j => j.Id)
            .Take(take)
            .Select(j => new { j.Id, j.FileName })
            .ToListAsync(cancellationToken);
        return rows.Select(r => (r.Id, r.FileName)).ToList();
    }

    private async Task<int> CountExpiredAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        return await ExpiredJobs(scope.ServiceProvider.GetRequiredService<AppDbContext>(), cutoffUtc).CountAsync(cancellationToken);
    }

    /// <summary>One page of the jobs whose upload is past the audio limit but whose transcript is still kept.</summary>
    private async Task<List<(Guid Id, string FileName)>> FindAudioExpiredAsync(
        DateTime audioCutoffUtc, DateTime? transcriptCutoffUtc, int skip, int take, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var query = ExpiredJobs(dbContext, audioCutoffUtc);
        if (transcriptCutoffUtc is { } transcriptCutoff)
            query = query.Where(j => (j.CompletedAtUtc ?? j.CreatedAtUtc) >= transcriptCutoff);

        var rows = await query
            .OrderBy(j => j.CompletedAtUtc ?? j.CreatedAtUtc)
            .ThenBy(j => j.Id)
            .Skip(skip).Take(take)
            .Select(j => new { j.Id, j.FileName })
            .ToListAsync(cancellationToken);
        return rows.Select(r => (r.Id, r.FileName)).ToList();
    }

    /// <summary>
    /// Finished, unlocked jobs with an owner whose completion (or creation) time is older than the cutoff.
    /// Jobs without an owner are skipped: nobody can lock them, an admin has to assign them first.
    /// </summary>
    private static IQueryable<Domain.Entities.AudioJob> ExpiredJobs(AppDbContext dbContext, DateTime cutoffUtc) =>
        dbContext.AudioJobs
            .AsNoTracking()
            .Where(j => j.OwnerId != null
                && !j.RetentionLocked
                && FinishedStatuses.Contains(j.Status)
                && (j.CompletedAtUtc ?? j.CreatedAtUtc) < cutoffUtc);
}
