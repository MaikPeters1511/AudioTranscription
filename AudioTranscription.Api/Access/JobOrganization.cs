using AudioTranscription.Api.Dtos;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Api.Access;

/// <summary>
/// Tags, folders and favorites of the requesting user (S20). All of it is personal, so every query here is
/// scoped to that user's id: someone else's tag or folder id matches nothing and reveals nothing.
/// </summary>
public static class JobOrganization
{
    /// <summary>
    /// Narrows a list of jobs the user may see to those matching all given filters. A tag or folder the user
    /// does not own simply matches no job. Folders only ever match jobs the user owns: a folder is the owner's
    /// own structure, a shared job does not appear in the reader's folders.
    /// </summary>
    public static IQueryable<AudioJob> Filter(
        this IQueryable<AudioJob> jobs, AppDbContext db, string userId, Guid? tagId, Guid? folderId, bool favoritesOnly)
    {
        if (tagId is { } tag)
        {
            jobs = jobs.Where(j => db.JobTags.Any(jt => jt.AudioJobId == j.Id && jt.TagId == tag
                && db.Tags.Any(t => t.Id == tag && t.OwnerId == userId)));
        }

        if (folderId is { } folder)
            jobs = jobs.Where(j => j.FolderId == folder && j.OwnerId == userId);

        if (favoritesOnly)
            jobs = jobs.Where(j => db.JobFavorites.Any(f => f.AudioJobId == j.Id && f.UserId == userId));

        return jobs;
    }

    /// <summary>The user's own tags on each of the given jobs.</summary>
    public static async Task<Dictionary<Guid, List<TagDto>>> TagsOfAsync(AppDbContext db, string userId, IReadOnlyCollection<Guid> jobIds)
    {
        var rows = await db.JobTags
            .Where(jt => jobIds.Contains(jt.AudioJobId))
            .Join(db.Tags.Where(t => t.OwnerId == userId), jt => jt.TagId, t => t.Id, (jt, t) => new { jt.AudioJobId, t.Id, t.Name, t.Color })
            .ToListAsync();

        return rows
            .GroupBy(r => r.AudioJobId)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.Name).Select(r => new TagDto(r.Id, r.Name, r.Color)).ToList());
    }

    /// <summary>The jobs among the given ones the user marked as favorite.</summary>
    public static async Task<HashSet<Guid>> FavoritesAmongAsync(AppDbContext db, string userId, IReadOnlyCollection<Guid> jobIds) =>
        (await db.JobFavorites
            .Where(f => f.UserId == userId && jobIds.Contains(f.AudioJobId))
            .Select(f => f.AudioJobId)
            .ToListAsync()).ToHashSet();
}
