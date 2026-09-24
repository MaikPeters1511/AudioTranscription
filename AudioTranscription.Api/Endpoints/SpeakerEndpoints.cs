using AudioTranscription.Api.Dtos;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Api.Endpoints;

/// <summary>Speakers detected by diarization (S11): list them and rename one.</summary>
public static class SpeakerEndpoints
{
    private const int MaxDisplayNameLength = 100;

    public static void MapSpeakerEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/audio-jobs/{id:guid}/speakers");

        group.MapGet("/", GetSpeakers)
            .WithName("GetJobSpeakers")
            .WithDescription("Speakers detected by diarization for a job, with their resolved display names");

        group.MapPut("/{index:int}", RenameSpeaker)
            .WithName("RenameJobSpeaker")
            .WithDescription("Rename a detected speaker (e.g. 'Sprecher 1' -> 'Anna')");
    }

    private static async Task<Results<Ok<List<JobSpeakerDto>>, NotFound<ProblemDetails>>> GetSpeakers(
        Guid id, AppDbContext dbContext)
    {
        if (!await dbContext.AudioJobs.AnyAsync(j => j.Id == id))
            return AudioJobEndpoints.JobNotFound(id);

        var speakers = await dbContext.JobSpeakers
            .Where(s => s.AudioJobId == id)
            .OrderBy(s => s.Index)
            .Select(s => new JobSpeakerDto(s.Index, s.DisplayName ?? SpeakerNaming.DefaultName(s.Index)))
            .ToListAsync();
        return TypedResults.Ok(speakers);
    }

    private static async Task<Results<NoContent, ValidationProblem, NotFound<ProblemDetails>>> RenameSpeaker(
        Guid id, int index, RenameSpeakerRequest request, AppDbContext dbContext)
    {
        var displayName = request.DisplayName?.Trim();
        if (string.IsNullOrEmpty(displayName) || displayName.Length > MaxDisplayNameLength)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["displayName"] = [$"Must be 1 to {MaxDisplayNameLength} characters."]
            });
        }

        var speaker = await dbContext.JobSpeakers.SingleOrDefaultAsync(s => s.AudioJobId == id && s.Index == index);
        if (speaker is null)
        {
            if (!await dbContext.AudioJobs.AnyAsync(j => j.Id == id))
                return AudioJobEndpoints.JobNotFound(id);

            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "Speaker not found",
                Detail = $"Job '{id}' has no speaker with index {index}.",
                Status = StatusCodes.Status404NotFound
            });
        }

        speaker.DisplayName = displayName;
        await dbContext.SaveChangesAsync();
        return TypedResults.NoContent();
    }
}
