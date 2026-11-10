using AudioTranscription.Api.Access;
using AudioTranscription.Api.BackgroundServices;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Api.Queue;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Api.Endpoints;

/// <summary>Queue overview and priority of waiting jobs (S34).</summary>
public static class QueueEndpoints
{
    public const int MinPriority = -100;
    public const int MaxPriority = 100;

    // The speed estimate rests on the most recent finished jobs so it follows hardware and model changes
    private const int SpeedSampleSize = 20;

    public static void MapQueueEndpoints(this WebApplication app)
    {
        app.MapGet("/api/queue", GetQueue)
            .WithTags("Queue")
            .WithName("GetQueue")
            .WithSummary("The caller's jobs in the transcription queue")
            .WithDescription("The running job (position 0) and waiting jobs (position 1 and up) of the signed-in user. " +
                             "Positions count the jobs of all users, but only own jobs are listed. Estimates (seconds) " +
                             "derive from the processing speed of recently finished jobs, using file size as a proxy for " +
                             "audio length, and are null while no job has finished yet.");

        app.MapPut("/api/audio-jobs/{id:guid}/priority", SetPriority)
            .RequireJobAccess()
            .WithTags("Queue")
            .WithName("SetJobPriority")
            .WithSummary("Change the priority of a waiting job")
            .WithDescription($"Priority {MinPriority} to {MaxPriority}, higher runs first, equal priorities run in upload order. " +
                             "Only pending jobs can be reprioritized; others answer 409.");
    }

    private static async Task<Results<Ok<QueueDto>, UnauthorizedHttpResult>> GetQueue(
        HttpContext http, AppDbContext db, TranscriptionQueue queue)
    {
        if (JobAccessService.GetUserId(http.User) is not { Length: > 0 } userId)
            return TypedResults.Unauthorized();

        var snapshot = queue.Snapshot();
        var snapshotIds = snapshot.Select(q => q.JobId).ToList();
        var running = await db.AudioJobs
            .Where(j => j.Status == AudioJobStatus.Processing)
            .OrderBy(j => j.StartedAtUtc)
            .Select(j => new { j.Id, j.FileName, j.FileSizeBytes, j.StartedAtUtc, j.Priority, j.OwnerId })
            .FirstOrDefaultAsync();
        // A job cancelled or deleted while waiting stays in the in-memory queue until the worker skips it
        var pending = await db.AudioJobs
            .Where(j => j.Status == AudioJobStatus.Pending && snapshotIds.Contains(j.Id))
            .Select(j => new { j.Id, j.FileName, j.FileSizeBytes, j.OwnerId })
            .ToDictionaryAsync(j => j.Id);
        var waiting = snapshot.Where(q => pending.ContainsKey(q.JobId)).ToList();

        var samples = await db.AudioJobs
            .Where(j => j.Status == AudioJobStatus.Completed && j.StartedAtUtc != null && j.CompletedAtUtc != null)
            .OrderByDescending(j => j.CompletedAtUtc)
            .Take(SpeedSampleSize)
            .Select(j => new { j.FileSizeBytes, j.StartedAtUtc, j.CompletedAtUtc })
            .ToListAsync();
        var speed = QueueEstimator.SecondsPerByte(samples.Select(s =>
            new FinishedRun(s.FileSizeBytes, s.CompletedAtUtc!.Value - s.StartedAtUtc!.Value)));

        var estimate = QueueEstimator.Estimate(
            speed,
            running?.StartedAtUtc is { } startedAt ? new RunningJob(running.FileSizeBytes, startedAt) : null,
            waiting.Select(w => pending[w.JobId].FileSizeBytes).ToList(),
            DateTime.UtcNow);

        var entries = new List<QueueEntryDto>();
        if (running is not null && running.OwnerId == userId)
        {
            entries.Add(new QueueEntryDto(
                running.Id, running.FileName, AudioJobStatus.Processing, 0, running.Priority,
                EstimatedWaitSeconds: 0, EstimatedProcessingSeconds: estimate.Running?.RemainingSeconds));
        }

        for (var i = 0; i < waiting.Count; i++)
        {
            var job = pending[waiting[i].JobId];
            if (job.OwnerId != userId)
                continue;
            entries.Add(new QueueEntryDto(
                job.Id, job.FileName, AudioJobStatus.Pending, i + 1, waiting[i].Priority,
                estimate.Waiting[i].WaitSeconds, estimate.Waiting[i].ProcessingSeconds));
        }

        return TypedResults.Ok(new QueueDto(entries, waiting.Count));
    }

    private static async Task<Results<NoContent, ValidationProblem, Conflict<Microsoft.AspNetCore.Mvc.ProblemDetails>>> SetPriority(
        Guid id, SetPriorityRequest request, AppDbContext db, TranscriptionQueue queue, ILogger<TranscriptionQueue> logger)
    {
        if (request.Priority is not { } priority || priority is < MinPriority or > MaxPriority)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["priority"] = [$"Priority must be a whole number between {MinPriority} and {MaxPriority}."]
            });
        }

        // The access filter already made sure the job exists and belongs to the caller
        var job = await db.AudioJobs.FindAsync(id);
        if (job!.Status != AudioJobStatus.Pending)
            return AudioJobEndpoints.JobConflict($"Only waiting jobs can be reprioritized (current status: {job.Status}).");

        job.Priority = priority;
        await db.SaveChangesAsync();
        // Not in the queue anymore means the worker just picked it up: the stored value still applies to a retry
        queue.SetPriority(id, priority);

        logger.LogInformation("Priority of job {JobId} set to {Priority}", id, priority);
        return TypedResults.NoContent();
    }
}
