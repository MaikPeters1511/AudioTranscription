using AudioTranscription.Api.Configuration;
using AudioTranscription.Api.Uploads;
using AudioTranscription.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudioTranscription.Api.BackgroundServices;

/// <summary>
/// Removes resumable uploads (S27, ADR 0006) that were never finished: the session row and its partial file go
/// after <see cref="UploadOptions.IncompleteSessionRetentionHours"/>. Finished sessions only lose their row;
/// their file is a normal upload from then on (see <see cref="OrphanedUploadCleanupService"/>).
/// </summary>
public class IncompleteUploadCleanupService(
    IServiceScopeFactory scopeFactory,
    ResumableUploadStore uploadStore,
    IOptions<UploadOptions> options,
    TimeProvider timeProvider,
    ILogger<IncompleteUploadCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        do
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failed run must not stop the service; the next run retries
                logger.LogWarning(ex, "Cleaning up incomplete uploads failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task CleanupAsync(CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddHours(-options.Value.IncompleteSessionRetentionHours);
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var expired = await db.UploadSessions.Where(s => s.CreatedAtUtc < cutoff).ToListAsync(cancellationToken);
        if (expired.Count == 0)
            return;

        var removedPartial = 0;
        foreach (var session in expired)
        {
            if (!session.IsClosed)
            {
                await uploadStore.Store.DeleteFileAsync(session.Id, cancellationToken);
                removedPartial++;
            }
        }

        db.UploadSessions.RemoveRange(expired);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Removed {Partial} incomplete upload(s) and {Total} upload session(s)", removedPartial, expired.Count);
    }
}
