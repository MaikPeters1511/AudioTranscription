using AudioTranscription.Api.Auth;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Api.Endpoints;

/// <summary>
/// Jobs from before ownership existed (S19) have no owner and are invisible to everyone. Admins can list
/// exactly those and give them to a user; they cannot see or take over jobs that already have an owner.
/// </summary>
public static class AdminJobEndpoints
{
    /// <summary>
    /// Serializes assignments so two admins cannot both give the same unassigned job away (the second one
    /// would silently take it over). Covers a single API instance, like the last-admin lock of S18.
    /// </summary>
    private static readonly SemaphoreSlim AssignLock = new(1, 1);

    public static void MapAdminJobEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/jobs")
            .WithTags("Admin")
            .RequireAuthorization(Roles.AdminPolicy);

        group.MapGet("/unassigned", GetUnassigned)
            .WithName("GetUnassignedJobs")
            .WithSummary("Jobs without an owner")
            .WithDescription("Jobs that predate ownership and have not been assigned to a user yet");

        group.MapPost("/{id:guid}/owner", AssignOwner)
            .WithName("AssignJobOwner")
            .WithSummary("Give an unassigned job to a user")
            .WithDescription("Only works for a job without an owner; a job that already has one is answered with 404, like for everyone else");
    }

    private static async Task<Ok<List<AudioJobListDto>>> GetUnassigned(AppDbContext dbContext)
    {
        var jobs = await dbContext.AudioJobs
            .Where(j => j.OwnerId == null)
            .OrderByDescending(j => j.CreatedAtUtc)
            .Select(j => new AudioJobListDto(
                j.Id, j.FileName, j.FileSizeBytes,
                j.Status, j.Language, j.DurationSeconds,
                j.CreatedAtUtc, j.CompletedAtUtc, null, null, null))
            .ToListAsync();
        return TypedResults.Ok(jobs);
    }

    private static async Task<Results<NoContent, NotFound<ProblemDetails>, ValidationProblem>> AssignOwner(
        Guid id, AssignOwnerRequest request, AppDbContext dbContext)
    {
        await AssignLock.WaitAsync();
        try
        {
            // A job that already has an owner is none of an admin's business (ADR 0007): not found, not 409,
            // which would confirm that someone else's job exists
            var job = await dbContext.AudioJobs.FindAsync(id);
            if (job is null || job.OwnerId is not null)
                return AudioJobEndpoints.JobNotFound(id);

            if (string.IsNullOrEmpty(request.UserId) || !await dbContext.Users.AnyAsync(u => u.Id == request.UserId))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["userId"] = ["No such user."]
                });
            }

            job.OwnerId = request.UserId;
            await dbContext.SaveChangesAsync();
            return TypedResults.NoContent();
        }
        finally
        {
            AssignLock.Release();
        }
    }
}
