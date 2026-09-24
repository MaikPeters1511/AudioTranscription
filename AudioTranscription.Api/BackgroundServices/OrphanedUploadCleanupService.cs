using AudioTranscription.Api.Configuration;
using AudioTranscription.Api.Storage;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudioTranscription.Api.BackgroundServices;

/// <summary>
/// Removes leftover uploads from temp storage once at startup, e.g. files of jobs that failed
/// before the finally-cleanup existed or intermediate WAV files of an interrupted conversion.
/// Uploads are named "{jobId}{extension}"; files of open jobs and recent files are always kept.
/// </summary>
public class OrphanedUploadCleanupService(
    IServiceScopeFactory scopeFactory,
    ITempFileStore tempFileStore,
    IOptions<UploadOptions> options,
    TimeProvider timeProvider,
    ILogger<OrphanedUploadCleanupService> logger) : IHostedService
{
    private readonly UploadOptions _options = options.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var directory = tempFileStore.StorageDirectory;
        if (!Directory.Exists(directory))
            return;

        var cutoffUtc = timeProvider.GetUtcNow().UtcDateTime.AddHours(-_options.OrphanedFileRetentionHours);
        var candidates = Directory.EnumerateFiles(directory)
            .Where(file => File.GetLastWriteTimeUtc(file) < cutoffUtc)
            .ToList();

        if (candidates.Count == 0)
            return;

        var protectedJobIds = await GetProtectedJobIdsAsync(cancellationToken);

        var deleted = 0;
        foreach (var file in candidates)
        {
            if (Guid.TryParse(Path.GetFileNameWithoutExtension(file), out var jobId) && protectedJobIds.Contains(jobId))
                continue;

            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Failed to delete orphaned upload {FilePath}", file);
            }
        }

        if (deleted > 0)
            logger.LogInformation("Deleted {Count} orphaned upload(s) from {Directory}", deleted, directory);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Jobs whose upload must survive: open jobs always, finished jobs only if uploads are kept by configuration.
    /// </summary>
    private async Task<HashSet<Guid>> GetProtectedJobIdsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var query = dbContext.AudioJobs.AsNoTracking();
        if (_options.DeleteAfterTranscription)
            query = query.Where(j => j.Status == AudioJobStatus.Pending || j.Status == AudioJobStatus.Processing);

        var ids = await query.Select(j => j.Id).ToListAsync(cancellationToken);
        return [.. ids];
    }
}
