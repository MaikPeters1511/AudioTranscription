using System.Threading.Channels;
using AudioTranscription.Api.Access;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Domain.Chapters;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using AudioTranscription.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Api.BackgroundServices;

/// <summary>Channel-based in-process queue for chapter generation (S25), separate from the transcription queue.</summary>
public class ChapterQueue
{
    private readonly Channel<ChapterRunRequest> _channel =
        Channel.CreateUnbounded<ChapterRunRequest>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public async ValueTask EnqueueAsync(ChapterRunRequest request, CancellationToken cancellationToken = default) =>
        await _channel.Writer.WriteAsync(request, cancellationToken);

    public IAsyncEnumerable<ChapterRunRequest> ReadAllAsync(CancellationToken cancellationToken = default) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

public record ChapterRunRequest(Guid RunId);

/// <summary>
/// Generates the chapters of a job (S25) from <see cref="ChapterQueue"/>, like <see cref="VariantWorker"/> does for
/// variants. The queue lives in memory, so runs still Pending after a restart are queued again when the service starts.
/// </summary>
public class ChapterWorker(
    ChapterQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<ChapterWorker> logger) : BackgroundService
{
    public const string ParseFailureMessage = "Das Sprachmodell hat keine verwertbaren Kapitel geliefert.";
    public const string GenerationFailureMessage = "Die Kapitel konnten nicht erzeugt werden. Ist das Sprachmodell erreichbar?";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverPendingRunsAsync(stoppingToken);

        await foreach (var request in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(request, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error processing chapter run {RunId}", request.RunId);
            }
        }
    }

    private async Task RecoverPendingRunsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pending = await db.ChapterRuns.Where(r => r.Status == VariantStatus.Pending).OrderBy(r => r.CreatedAtUtc).ToListAsync(cancellationToken);
        foreach (var run in pending)
            await queue.EnqueueAsync(new ChapterRunRequest(run.Id), cancellationToken);
        if (pending.Count > 0)
            logger.LogInformation("Recovered {Count} pending chapter run(s) after restart", pending.Count);
    }

    internal async Task ProcessAsync(ChapterRunRequest request, CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var generator = scope.ServiceProvider.GetRequiredService<IChapterGenerator>();
        var notifier = scope.ServiceProvider.GetRequiredService<IJobNotifier>();

        var run = await db.ChapterRuns.FindAsync([request.RunId], stoppingToken);
        if (run is null || run.Status != VariantStatus.Pending)
            return;

        var segments = await db.TranscriptSegments
            .Where(s => s.AudioJobId == run.AudioJobId)
            .OrderBy(s => s.Index)
            .ToListAsync(stoppingToken);
        var chapterSegments = segments.Select(s => new ChapterSegment(s.Index, s.StartMs, s.EndMs, s.EffectiveText)).ToList();

        try
        {
            var drafts = await generator.GenerateAsync(chapterSegments, stoppingToken);
            var chapters = ChapterBuilder.Build(run.AudioJobId, chapterSegments, drafts);
            if (chapters.Count == 0)
                throw new ChapterParseException("No chapters.");

            // The new chapters replace the old ones (also hand-edited ones) in one save
            db.TranscriptChapters.RemoveRange(await db.TranscriptChapters.Where(c => c.AudioJobId == run.AudioJobId).ToListAsync(stoppingToken));
            db.TranscriptChapters.AddRange(chapters);
            run.Status = VariantStatus.Completed;
            run.ErrorMessage = null;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw; // shutdown: the run stays Pending and is queued again after the restart
        }
        catch (ChapterParseException ex)
        {
            logger.LogWarning(ex, "Chapter run {RunId} of job {JobId}: unusable answer", run.Id, run.AudioJobId);
            run.Status = VariantStatus.Failed;
            run.ErrorMessage = ParseFailureMessage;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Chapter run {RunId} of job {JobId} failed", run.Id, run.AudioJobId);
            run.Status = VariantStatus.Failed;
            run.ErrorMessage = GenerationFailureMessage;
        }

        run.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(stoppingToken);
        await notifier.NotifyAsync(run.AudioJobId, "ChaptersCompleted", new ChaptersCompletedDto(run.AudioJobId, run.Id, run.Status), stoppingToken);
    }
}
