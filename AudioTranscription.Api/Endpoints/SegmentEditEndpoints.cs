using AudioTranscription.Api.Access;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Api.Endpoints;

/// <summary>
/// Correcting transcript segments with a version history (S32). The original text of a segment and the
/// job's raw transcript are never changed; corrections are stored beside them. Editing and restoring need
/// owner access (S19), reading the history works for everyone who may read the job.
/// </summary>
public static class SegmentEditEndpoints
{
    public const int MaxTextLength = 10_000;

    public static void MapSegmentEditEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/audio-jobs/{id:guid}/segments/{index:int}")
            .WithTags("Segment edits")
            .RequireJobAccess();

        group.MapPut("", Edit)
            .WithName("EditSegment")
            .WithSummary("Correct a segment")
            .WithDescription("Stores the text as a new version of the segment; 409 if expectedVersion is not the current version");

        group.MapGet("/history", History)
            .WithName("GetSegmentHistory")
            .WithSummary("Version history of a segment")
            .WithDescription("All versions of a segment, newest first; version 0 is the original");

        group.MapPost("/restore", Restore)
            .WithName("RestoreSegment")
            .WithSummary("Restore an earlier version")
            .WithDescription("Creates a new version with the text of an earlier one (version 0 = original)");
    }

    private static async Task<Results<Ok<TranscriptSegmentDto>, ValidationProblem, NotFound<ProblemDetails>, Conflict<ProblemDetails>>> Edit(
        Guid id, int index, EditSegmentRequest request, HttpContext http, AppDbContext dbContext)
    {
        var text = request.Text?.Trim() ?? string.Empty;
        if (text.Length == 0 || text.Length > MaxTextLength)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["text"] = [$"The text must not be empty and at most {MaxTextLength} characters long."]
            });
        }

        return await ApplyAsync(id, index, request.ExpectedVersion, _ => text, http, dbContext);
    }

    private static async Task<Results<Ok<TranscriptSegmentDto>, NotFound<ProblemDetails>, Conflict<ProblemDetails>>> Restore(
        Guid id, int index, RestoreSegmentRequest request, HttpContext http, AppDbContext dbContext)
    {
        // Resolve the text of the requested version first so an unknown version is a 404, not a conflict
        var segment = await FindSegmentAsync(id, index, dbContext);
        if (segment is null)
            return SegmentNotFound(index);
        string? restoredText;
        if (request.Version == 0)
        {
            restoredText = segment.Text;
        }
        else
        {
            restoredText = await dbContext.SegmentEdits
                .Where(e => e.SegmentId == segment.Id && e.Version == request.Version)
                .Select(e => e.Text)
                .FirstOrDefaultAsync();
        }
        if (restoredText is null)
            return SegmentNotFound(index, $"Version {request.Version} of segment {index} does not exist.");

        var result = await ApplyAsync(id, index, request.ExpectedVersion, _ => restoredText, http, dbContext);
        return result.Result switch
        {
            Ok<TranscriptSegmentDto> ok => ok,
            Conflict<ProblemDetails> conflict => conflict,
            NotFound<ProblemDetails> notFound => notFound,
            _ => throw new InvalidOperationException("Unexpected result when restoring a segment.")
        };
    }

    private static async Task<Results<Ok<List<SegmentVersionDto>>, NotFound<ProblemDetails>>> History(
        Guid id, int index, AppDbContext dbContext)
    {
        var segment = await FindSegmentAsync(id, index, dbContext);
        if (segment is null)
            return SegmentNotFound(index);

        var edits = await dbContext.SegmentEdits
            .Where(e => e.SegmentId == segment.Id)
            .OrderByDescending(e => e.Version)
            .ToListAsync();

        var history = edits
            .Select(e => new SegmentVersionDto(e.Version, e.Text, e.EditedBy, e.EditedAtUtc))
            .Append(new SegmentVersionDto(0, segment.Text, null, null))
            .ToList();
        return TypedResults.Ok(history);
    }

    private static async Task<Results<Ok<TranscriptSegmentDto>, ValidationProblem, NotFound<ProblemDetails>, Conflict<ProblemDetails>>> ApplyAsync(
        Guid id, int index, int expectedVersion, Func<TranscriptSegment, string> newText, HttpContext http, AppDbContext dbContext)
    {
        var job = await dbContext.AudioJobs.FindAsync(id);
        if (job is null)
            return AudioJobEndpoints.JobNotFound(id);
        if (job.Status != AudioJobStatus.Completed)
            return AudioJobEndpoints.JobConflict($"Segments can only be edited for completed jobs (current status: {job.Status}).");

        var segment = await FindSegmentAsync(id, index, dbContext);
        if (segment is null)
            return SegmentNotFound(index);
        if (segment.Version != expectedVersion)
        {
            return AudioJobEndpoints.JobConflict(
                $"Segment {index} was changed in the meantime (current version {segment.Version}, expected {expectedVersion}).");
        }

        var text = newText(segment);
        segment.Version++;
        segment.CurrentText = text;
        dbContext.SegmentEdits.Add(new SegmentEdit
        {
            SegmentId = segment.Id,
            Version = segment.Version,
            Text = text,
            EditedBy = JobAccessService.GetUserId(http.User),
        });

        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // A parallel edit took the same version number (unique index on SegmentId + Version)
            return AudioJobEndpoints.JobConflict($"Segment {index} was changed in the meantime.");
        }

        var speakerName = segment.SpeakerIndex is { } speaker
            ? (await SpeakerNaming.LoadResolvedNamesAsync(dbContext, id)).GetValueOrDefault(speaker, SpeakerNaming.DefaultName(speaker))
            : null;
        return TypedResults.Ok(new TranscriptSegmentDto(
            segment.Index, segment.StartMs, segment.EndMs, segment.EffectiveText, segment.SpeakerIndex, speakerName, segment.Version));
    }

    private static Task<TranscriptSegment?> FindSegmentAsync(Guid jobId, int index, AppDbContext dbContext) =>
        dbContext.TranscriptSegments.FirstOrDefaultAsync(s => s.AudioJobId == jobId && s.Index == index);

    private static NotFound<ProblemDetails> SegmentNotFound(int index, string? detail = null) =>
        TypedResults.NotFound(new ProblemDetails
        {
            Title = "Segment not found",
            Detail = detail ?? $"Segment {index} does not exist.",
            Status = StatusCodes.Status404NotFound
        });
}
