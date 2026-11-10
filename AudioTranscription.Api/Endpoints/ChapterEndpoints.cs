using AudioTranscription.Api.Access;
using AudioTranscription.Api.BackgroundServices;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Domain.Chapters;
using AudioTranscription.Infrastructure.Data;
using AudioTranscription.Infrastructure.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Api.Endpoints;

/// <summary>
/// Automatic chapters of a recording (S25). Everyone who may read the job sees them; generating, editing and
/// deleting need owner access (S19). Generation runs in the background like a transcript variant (S10).
/// </summary>
public static class ChapterEndpoints
{
    public const int MaxTitleLength = ChapterResponseParser.MaxTitleLength;
    public const int MaxSummaryLength = ChapterResponseParser.MaxSummaryLength;

    public static void MapChapterEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/audio-jobs/{id:guid}/chapters")
            .WithTags("Chapters")
            .RequireJobAccess();

        group.MapPost("", Generate)
            .WithName("GenerateChapters")
            .WithSummary("Generate chapters")
            .WithDescription("Starts the generation (or regeneration, which replaces all chapters) in the background; requires a completed job and a configured LLM");
        group.MapGet("", Get)
            .WithName("GetChapters")
            .WithSummary("Chapters of a job")
            .WithDescription("The chapters in order together with the state of their generation (None, Pending, Completed, Failed)");
        group.MapPut("{chapterId:guid}", Update)
            .WithName("UpdateChapter")
            .WithSummary("Edit title and summary of a chapter");
        group.MapDelete("{chapterId:guid}", Delete)
            .WithName("DeleteChapter")
            .WithSummary("Delete a chapter")
            .WithDescription("The neighbouring chapter takes over its time range, so the chapters stay gap-free");
    }

    private static async Task<Results<Accepted<ChaptersDto>, NotFound<ProblemDetails>, Conflict<ProblemDetails>, StatusCodeHttpResult>> Generate(
        Guid id, AppDbContext db, IServiceProvider services)
    {
        var job = await db.AudioJobs.FindAsync(id);
        if (job is null)
            return AudioJobEndpoints.JobNotFound(id);
        if (job.Status != AudioJobStatus.Completed)
            return AudioJobEndpoints.JobConflict($"Chapters can only be generated for completed jobs (current status: {job.Status}).");
        if (!await db.TranscriptSegments.AnyAsync(s => s.AudioJobId == id))
            return AudioJobEndpoints.JobConflict("This job has no timed segments to divide into chapters.");

        var generator = services.GetService<IChapterGenerator>();
        var queue = services.GetService<ChapterQueue>();
        if (generator is null || queue is null)
            return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);

        var run = await db.ChapterRuns.SingleOrDefaultAsync(r => r.AudioJobId == id);
        if (run is { Status: VariantStatus.Pending })
            return TypedResults.Accepted($"/api/audio-jobs/{id}/chapters", await LoadAsync(db, id)); // already on its way

        if (run is null)
        {
            run = new ChapterRun { AudioJobId = id };
            db.ChapterRuns.Add(run);
        }
        else
        {
            run.Status = VariantStatus.Pending;
            run.ErrorMessage = null;
            run.CreatedAtUtc = DateTime.UtcNow;
            run.CompletedAtUtc = null;
        }
        await db.SaveChangesAsync();
        await queue.EnqueueAsync(new ChapterRunRequest(run.Id));

        return TypedResults.Accepted($"/api/audio-jobs/{id}/chapters", await LoadAsync(db, id));
    }

    private static async Task<Ok<ChaptersDto>> Get(Guid id, AppDbContext db) =>
        TypedResults.Ok(await LoadAsync(db, id));

    private static async Task<Results<Ok<ChapterDto>, ValidationProblem, NotFound<ProblemDetails>>> Update(
        Guid id, Guid chapterId, UpdateChapterRequest request, AppDbContext db)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        var summary = request.Summary?.Trim() ?? string.Empty;
        var errors = new Dictionary<string, string[]>();
        if (title.Length == 0 || title.Length > MaxTitleLength)
            errors["title"] = [$"The title must not be empty and at most {MaxTitleLength} characters long."];
        if (summary.Length > MaxSummaryLength)
            errors["summary"] = [$"The summary must be at most {MaxSummaryLength} characters long."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var chapter = await db.TranscriptChapters.SingleOrDefaultAsync(c => c.Id == chapterId && c.AudioJobId == id);
        if (chapter is null)
            return ChapterNotFound(chapterId);

        chapter.Title = title;
        chapter.Summary = summary;
        await db.SaveChangesAsync();
        return TypedResults.Ok(ToDto(chapter));
    }

    private static async Task<Results<NoContent, NotFound<ProblemDetails>>> Delete(Guid id, Guid chapterId, AppDbContext db)
    {
        var chapters = await db.TranscriptChapters.Where(c => c.AudioJobId == id).OrderBy(c => c.Position).ToListAsync();
        var index = chapters.FindIndex(c => c.Id == chapterId);
        if (index < 0)
            return ChapterNotFound(chapterId);

        var removed = chapters[index];
        // The neighbour takes over the time range so the chapters keep following each other without a gap
        if (index > 0)
            chapters[index - 1].EndMs = removed.EndMs;
        else if (chapters.Count > 1)
            chapters[1].StartMs = removed.StartMs;

        db.TranscriptChapters.Remove(removed);
        chapters.RemoveAt(index);
        for (var i = 0; i < chapters.Count; i++)
            chapters[i].Position = i;

        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<ChaptersDto> LoadAsync(AppDbContext db, Guid jobId)
    {
        var run = await db.ChapterRuns.AsNoTracking().SingleOrDefaultAsync(r => r.AudioJobId == jobId);
        var chapters = await db.TranscriptChapters.AsNoTracking()
            .Where(c => c.AudioJobId == jobId)
            .OrderBy(c => c.Position)
            .ToListAsync();
        return new ChaptersDto(run?.Status.ToString() ?? "None", run?.ErrorMessage, chapters.Select(ToDto).ToList());
    }

    private static ChapterDto ToDto(TranscriptChapter c) => new(c.Id, c.Position, c.StartMs, c.EndMs, c.Title, c.Summary);

    private static NotFound<ProblemDetails> ChapterNotFound(Guid chapterId) =>
        TypedResults.NotFound(new ProblemDetails
        {
            Title = "Chapter not found",
            Detail = $"Chapter {chapterId} does not exist.",
            Status = StatusCodes.Status404NotFound
        });
}
