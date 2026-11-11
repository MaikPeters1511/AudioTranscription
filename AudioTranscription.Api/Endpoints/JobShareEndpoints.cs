using AudioTranscription.Api.Access;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Api.Endpoints;

/// <summary>The owner of a job shares it read-only with other users (S19, ADR 0007).</summary>
public static class JobShareEndpoints
{
    public const string RateLimitPolicy = "share-lookup";

    public static void MapJobShareEndpoints(this WebApplication app)
    {
        // Only the owner manages shares, whatever the HTTP method
        var group = app.MapGroup("/api/audio-jobs/{id:guid}/shares")
            .WithTags("Sharing")
            .RequireJobAccess(JobAccess.Owner);

        group.MapGet("/", GetShares)
            .WithName("GetJobShares")
            .WithSummary("Users a job is shared with")
            .WithDescription("Users the job is shared with (read only); owner only");

        group.MapPost("/", ShareJob)
            .RequireRateLimiting(RateLimitPolicy)
            .WithName("ShareJob")
            .WithSummary("Share a job read-only with a user")
            .WithDescription("Gives the user with the given e-mail address read access; repeating it changes nothing; owner only");

        group.MapDelete("/{userId}", RevokeShare)
            .WithName("RevokeJobShare")
            .WithSummary("Stop sharing a job with a user")
            .WithDescription("Removes the read access of the user; owner only");
    }

    private static async Task<Ok<List<JobShareDto>>> GetShares(Guid id, AppDbContext dbContext)
    {
        var shares = await dbContext.JobShares
            .Where(s => s.AudioJobId == id)
            .Join(dbContext.Users, s => s.UserId, u => u.Id, (s, u) => new { s.SharedAtUtc, u.Id, u.Email, u.UserName })
            .OrderBy(x => x.SharedAtUtc)
            .ToListAsync();

        return TypedResults.Ok(shares.Select(x => new JobShareDto(x.Id, x.Email ?? x.UserName ?? string.Empty)).ToList());
    }

    private static async Task<Results<NoContent, ValidationProblem>> ShareJob(
        Guid id,
        ShareJobRequest request,
        HttpContext http,
        AppDbContext dbContext,
        UserManager<IdentityUser> users)
    {
        var email = request.Email?.Trim();
        var target = string.IsNullOrEmpty(email) ? null : await users.FindByEmailAsync(email);
        if (target is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["email"] = ["No user with this e-mail address."]
            });
        }
        if (target.Id == JobAccessService.GetUserId(http.User))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["email"] = ["The job already belongs to you."]
            });
        }

        if (!await dbContext.JobShares.AnyAsync(s => s.AudioJobId == id && s.UserId == target.Id))
        {
            dbContext.JobShares.Add(new JobShare { AudioJobId = id, UserId = target.Id });
            try
            {
                await dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Shared in parallel by a second request: the share exists, which is what was asked for.
                // Any other failure is rethrown for the global exception handler.
                dbContext.ChangeTracker.Clear();
                if (!await dbContext.JobShares.AsNoTracking().AnyAsync(s => s.AudioJobId == id && s.UserId == target.Id))
                    throw;
            }
        }

        return TypedResults.NoContent();
    }

    private static async Task<NoContent> RevokeShare(Guid id, string userId, AppDbContext dbContext, IJobNotifier notifier)
    {
        var share = await dbContext.JobShares.SingleOrDefaultAsync(s => s.AudioJobId == id && s.UserId == userId);
        if (share is not null)
        {
            dbContext.JobShares.Remove(share);
            await dbContext.SaveChangesAsync();

            // The user no longer sees the job: their open list and detail view drop it at once (S19-F1)
            await notifier.SendAsync([userId], "JobUnshared", id);
        }
        return TypedResults.NoContent();
    }
}
