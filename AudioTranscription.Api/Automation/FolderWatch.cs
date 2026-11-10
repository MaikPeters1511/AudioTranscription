using AudioTranscription.Api.Configuration;
using AudioTranscription.Api.Storage;
using AudioTranscription.Api.Uploads;
using AudioTranscription.Api.Validation;
using AudioTranscription.Domain.Automation;
using AudioTranscription.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudioTranscription.Api.Automation;

/// <summary>Configuration of the watched folders (S28, ADR 0009), section <c>Watch</c>. Operators set it, users cannot.</summary>
public class WatchOptions
{
    public const string SectionName = "Watch";

    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 10;
    /// <summary>A file is picked up only when it has not changed for this long, so half-copied files are not read.</summary>
    public int StableSeconds { get; set; } = 5;
    public List<WatchFolderOptions> Folders { get; set; } = [];
}

public class WatchFolderOptions
{
    /// <summary>Absolute path of the folder; only its direct files are looked at.</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>E-mail address (or user name) of the user who owns the resulting jobs.</summary>
    public string User { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string? Language { get; set; }
}

/// <summary>Stops the start with a clear message for folders that could not work or would overlap.</summary>
public class WatchOptionsValidator : IValidateOptions<WatchOptions>
{
    public ValidateOptionsResult Validate(string? name, WatchOptions options)
    {
        var errors = new List<string>();
        if (options.PollIntervalSeconds < 1)
            errors.Add("Watch:PollIntervalSeconds must be at least 1.");
        if (options.StableSeconds < 0)
            errors.Add("Watch:StableSeconds must not be negative.");

        var paths = new List<string>();
        for (var i = 0; i < options.Folders.Count; i++)
        {
            var folder = options.Folders[i];
            if (string.IsNullOrWhiteSpace(folder.Path) || !System.IO.Path.IsPathRooted(folder.Path))
            {
                errors.Add($"Watch:Folders:{i}:Path must be an absolute path.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(folder.User))
                errors.Add($"Watch:Folders:{i}:User (the target user's e-mail address) is required.");
            paths.Add(Normalize(folder.Path));
        }

        // Two folders that are the same or nested would import the same file twice
        for (var i = 0; i < paths.Count; i++)
            for (var j = i + 1; j < paths.Count; j++)
                if (IsSameOrInside(paths[i], paths[j]) || IsSameOrInside(paths[j], paths[i]))
                    errors.Add("Watch:Folders must not contain the same folder twice or a folder inside another one.");

        return errors.Count > 0 ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
    }

    internal static string Normalize(string path) =>
        System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

    private static bool IsSameOrInside(string path, string parent) =>
        path.Equals(parent, StringComparison.Ordinal) ||
        path.StartsWith(parent + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal);
}

/// <summary>
/// One pass over the watched folders (ADR 0009): checks each stable file, creates a job for the target user the same way an
/// upload does, and moves the original to <c>processed</c> (or <c>rejected</c>). The job id derives from the file, so a pass
/// repeated after a crash recognizes files it already imported instead of transcribing them twice.
/// </summary>
public class FolderWatchProcessor(
    IOptions<WatchOptions> watchOptions,
    IOptions<UploadOptions> uploadOptions,
    UploadJobService uploads,
    UserManager<IdentityUser> users,
    AppDbContext db,
    ITempFileStore tempFiles,
    TimeProvider time,
    ILogger<FolderWatchProcessor> logger)
{
    public const string ProcessedFolder = "processed";
    public const string RejectedFolder = "rejected";

    /// <returns>The number of files that became new jobs.</returns>
    public async Task<int> ScanOnceAsync(CancellationToken cancellationToken)
    {
        var imported = 0;
        foreach (var folder in watchOptions.Value.Folders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                imported += await ScanFolderAsync(folder, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Only the type: the message contains the path
                logger.LogWarning("Watched folder #{Index} could not be read ({ExceptionType})", watchOptions.Value.Folders.IndexOf(folder), ex.GetType().Name);
            }
        }
        return imported;
    }

    private async Task<int> ScanFolderAsync(WatchFolderOptions folder, CancellationToken cancellationToken)
    {
        var index = watchOptions.Value.Folders.IndexOf(folder);
        var root = WatchOptionsValidator.Normalize(folder.Path);
        if (!Directory.Exists(root))
        {
            logger.LogWarning("Watched folder #{Index} does not exist", index);
            return 0;
        }

        var user = await users.FindByEmailAsync(folder.User) ?? await users.FindByNameAsync(folder.User);
        if (user is null || user.LockoutEnd == DateTimeOffset.MaxValue)
        {
            logger.LogWarning("Watched folder #{Index}: the target user does not exist or is deactivated; files stay where they are", index);
            return 0;
        }

        var (settings, errors) = await uploads.ResolveSettingsAsync(
            new RequestedUploadSettings(folder.Model, folder.Language, Diarize: false, GlossaryId: null), user.Id, cancellationToken);
        if (settings is null)
        {
            logger.LogWarning("Watched folder #{Index}: invalid transcription settings ({Fields})", index, string.Join(", ", errors.Keys));
            return 0;
        }

        var imported = 0;
        var stableFor = TimeSpan.FromSeconds(watchOptions.Value.StableSeconds);
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            var name = info.Name;

            // Never leave the folder: no links, no hidden files, nothing that is not directly inside the root
            if (name.StartsWith('.') || info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                !string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), root, StringComparison.Ordinal))
                continue;
            if (time.GetUtcNow().UtcDateTime - info.LastWriteTimeUtc < stableFor)
                continue;

            if (await ImportAsync(info, root, user.Id, settings, cancellationToken))
                imported++;
        }
        return imported;
    }

    /// <returns>True if a new job was created.</returns>
    private async Task<bool> ImportAsync(FileInfo file, string root, string ownerId, ResolvedUploadSettings settings, CancellationToken cancellationToken)
    {
        var safeName = WatchFileRules.SafeFileName(file.Name);
        if (!WatchFileRules.TryGetContentType(safeName, out var contentType))
        {
            MoveAway(file, root, RejectedFolder, safeName, Guid.NewGuid());
            return false;
        }
        if (file.Length > uploadOptions.Value.MaxFileSizeBytes || !HasAudioSignature(file, contentType))
        {
            MoveAway(file, root, RejectedFolder, safeName, Guid.NewGuid());
            return false;
        }

        var jobId = WatchFileRules.JobIdFor(root, file.Name, file.Length, file.LastWriteTimeUtc.Ticks);
        if (await db.AudioJobs.AnyAsync(j => j.Id == jobId, cancellationToken))
        {
            // Imported before, but the move did not happen (crash): only the move is missing
            MoveAway(file, root, ProcessedFolder, safeName, jobId);
            return false;
        }

        var uploadPath = tempFiles.GetUploadPath(jobId, safeName);
        Directory.CreateDirectory(tempFiles.StorageDirectory);
        File.Copy(file.FullName, uploadPath, overwrite: true);
        try
        {
            await uploads.CreateAndEnqueueAsync(jobId, ownerId, safeName, file.Length, contentType, uploadPath, settings);
        }
        catch
        {
            File.Delete(uploadPath);
            throw;
        }

        MoveAway(file, root, ProcessedFolder, safeName, jobId);
        // Only the id is logged: file names can contain personal data
        logger.LogInformation("Watched folder: job {JobId} created from a dropped file", jobId);
        return true;
    }

    private static bool HasAudioSignature(FileInfo file, string contentType)
    {
        using var stream = file.OpenRead();
        return MagicBytesValidator.IsValid(contentType, stream);
    }

    /// <summary>Moves the original into a subfolder; a failure is logged and retried at the next pass.</summary>
    private void MoveAway(FileInfo file, string root, string subfolder, string safeName, Guid id)
    {
        try
        {
            var targetDirectory = Path.Combine(root, subfolder);
            Directory.CreateDirectory(targetDirectory);
            var target = Path.GetFullPath(Path.Combine(targetDirectory, $"{id:N}_{safeName}"));
            if (!target.StartsWith(targetDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new IOException("Target path leaves the folder.");
            File.Move(file.FullName, target, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("A watched file could not be moved to '{Subfolder}' ({ExceptionType}); trying again at the next pass", subfolder, ex.GetType().Name);
        }
    }
}

/// <summary>Runs <see cref="FolderWatchProcessor"/> at an interval. Registered only if folders are configured.</summary>
public class FolderWatchService(IServiceScopeFactory scopes, IOptions<WatchOptions> options, ILogger<FolderWatchService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.PollIntervalSeconds)));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<FolderWatchProcessor>().ScanOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Folder watch run failed; trying again at the next interval");
            }
        }
        while (await NextTickAsync(timer, stoppingToken));
    }

    private static async Task<bool> NextTickAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
