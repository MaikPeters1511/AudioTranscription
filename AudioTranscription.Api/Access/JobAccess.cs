using System.Security.Claims;
using AudioTranscription.Api.Endpoints;
using AudioTranscription.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Api.Access;

/// <summary>What a user may do with a job (ADR 0007). The order matters: a higher level includes the lower ones.</summary>
public enum JobAccess
{
    None = 0,

    /// <summary>The job was shared with the user: read only.</summary>
    Read = 1,

    /// <summary>The user uploaded the job: read, change, delete and share.</summary>
    Owner = 2,
}

/// <summary>
/// The one place that decides who may see or change a job (S19, ADR 0007). Endpoints do not
/// check ownership themselves; they use <see cref="JobAccessEndpointExtensions.RequireJobAccess{TBuilder}"/>
/// or, for lists and search, <see cref="VisibleJobIds"/>.
/// </summary>
public class JobAccessService(AppDbContext db)
{
    /// <summary>The Identity user id of the signed-in user, or null.</summary>
    public static string? GetUserId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>Ids of the jobs a user may read: the own ones and those shared with them.</summary>
    public IQueryable<Guid> VisibleJobIds(string userId) =>
        db.AudioJobs
            .Where(j => j.OwnerId == userId || db.JobShares.Any(s => s.AudioJobId == j.Id && s.UserId == userId))
            .Select(j => j.Id);

    public async Task<JobAccess> GetAccessAsync(Guid jobId, string? userId)
    {
        if (string.IsNullOrEmpty(userId))
            return JobAccess.None;

        var owner = await db.AudioJobs
            .Where(j => j.Id == jobId)
            .Select(j => new { j.OwnerId })
            .SingleOrDefaultAsync();
        if (owner is null)
            return JobAccess.None;
        if (owner.OwnerId == userId)
            return JobAccess.Owner;

        return await db.JobShares.AnyAsync(s => s.AudioJobId == jobId && s.UserId == userId)
            ? JobAccess.Read
            : JobAccess.None;
    }
}

public static class JobAccessEndpointExtensions
{
    /// <summary>
    /// Guards every route below a <c>{id:guid}</c> job route. Without <paramref name="required"/>, reading
    /// (GET/HEAD) needs <see cref="JobAccess.Read"/> and everything else <see cref="JobAccess.Owner"/>.
    /// Someone without any access gets 404, exactly like for a job that does not exist; someone with
    /// read access to a route that needs more gets 403.
    /// </summary>
    public static TBuilder RequireJobAccess<TBuilder>(this TBuilder builder, JobAccess? required = null)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (!Guid.TryParse(http.GetRouteValue("id")?.ToString(), out var jobId))
                return await next(context);

            var needed = required ?? (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method)
                ? JobAccess.Read
                : JobAccess.Owner);

            var access = await http.RequestServices.GetRequiredService<JobAccessService>()
                .GetAccessAsync(jobId, JobAccessService.GetUserId(http.User));

            if (access == JobAccess.None)
                return AudioJobEndpoints.JobNotFound(jobId);
            if (access < needed)
            {
                return Results.Problem(
                    title: "Forbidden",
                    detail: "Only the owner of this job may do that.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            return await next(context);
        });
        return builder;
    }
}
