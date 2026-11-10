using System.Text.RegularExpressions;
using AudioTranscription.Api.Access;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Api.Endpoints;

/// <summary>
/// Tags, folders and favorites (S20). They are personal: tags and favorites work on every job the user may
/// read (also shared ones) and are invisible to everyone else; folders sort the user's own jobs.
/// </summary>
public static partial class JobOrganizationEndpoints
{
    private const int MaxTagNameLength = 50;
    private const int MaxFolderNameLength = 100;
    private const int MaxTagsPerJob = 50;

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();

    public static void MapJobOrganizationEndpoints(this WebApplication app)
    {
        // ---- tags
        var tags = app.MapGroup("/api/tags").WithTags("Organization");
        tags.MapGet("/", GetTags)
            .WithName("GetTags").WithSummary("The user's tags").WithDescription("The tags of the signed-in user");
        tags.MapPost("/", CreateTag)
            .WithName("CreateTag").WithSummary("Create a tag").WithDescription("Creates a tag with a name (unique per user, case-insensitive) and an optional #RRGGBB color");
        tags.MapDelete("/{id:guid}", DeleteTag)
            .WithName("DeleteTag").WithSummary("Delete a tag").WithDescription("Deletes the tag and removes it from all jobs");

        // ---- folders
        var folders = app.MapGroup("/api/folders").WithTags("Organization");
        folders.MapGet("/", GetFolders)
            .WithName("GetFolders").WithSummary("The user's folders").WithDescription("All folders of the signed-in user as a flat list with parent ids");
        folders.MapPost("/", CreateFolder)
            .WithName("CreateFolder").WithSummary("Create a folder").WithDescription("Creates a folder, optionally inside another one; names are unique within a parent");
        folders.MapPut("/{id:guid}", UpdateFolder)
            .WithName("UpdateFolder").WithSummary("Rename or move a folder").WithDescription("Renames and/or moves a folder; it cannot move into itself or its own subfolders");
        folders.MapDelete("/{id:guid}", DeleteFolder)
            .WithName("DeleteFolder").WithSummary("Delete a folder").WithDescription("Its jobs move to the top level, its subfolders up to the deleted folder's parent");

        // ---- per job: readers may tag and favorite (personally), only the owner files
        var readable = app.MapGroup("/api/audio-jobs/{id:guid}").WithTags("Organization").RequireJobAccess(JobAccess.Read);
        readable.MapPut("/tags", SetJobTags)
            .WithName("SetJobTags").WithSummary("Set the user's tags on a job").WithDescription("Replaces the user's own tags on the job; tags of other users are untouched");
        readable.MapPut("/favorite", AddFavorite)
            .WithName("AddFavorite").WithSummary("Mark a job as favorite").WithDescription("Personal favorite mark; repeating it changes nothing");
        readable.MapDelete("/favorite", RemoveFavorite)
            .WithName("RemoveFavorite").WithSummary("Remove the favorite mark").WithDescription("Repeating it changes nothing");

        var owned = app.MapGroup("/api/audio-jobs/{id:guid}").WithTags("Organization").RequireJobAccess(JobAccess.Owner);
        owned.MapPut("/folder", SetJobFolder)
            .WithName("SetJobFolder").WithSummary("File a job in a folder").WithDescription("Files the job in one of the owner's folders, or at the top level with a null folderId; owner only");
    }

    private static string? UserId(HttpContext http) => JobAccessService.GetUserId(http.User);

    // ================================================================ tags

    private static async Task<Results<Ok<List<TagDto>>, UnauthorizedHttpResult>> GetTags(HttpContext http, AppDbContext db)
    {
        if (UserId(http) is not { Length: > 0 } userId)
            return TypedResults.Unauthorized();

        var tags = await db.Tags.Where(t => t.OwnerId == userId).OrderBy(t => t.Name).ToListAsync();
        return TypedResults.Ok(tags.Select(ToDto).ToList());
    }

    private static async Task<Results<Created<TagDto>, ValidationProblem, Conflict<string>, UnauthorizedHttpResult>> CreateTag(
        CreateTagRequest request, HttpContext http, AppDbContext db)
    {
        if (UserId(http) is not { Length: > 0 } userId)
            return TypedResults.Unauthorized();

        var errors = new Dictionary<string, string[]>();
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxTagNameLength)
            errors["name"] = [$"Must be 1 to {MaxTagNameLength} characters."];
        if (request.Color is not null && !HexColor().IsMatch(request.Color))
            errors["color"] = ["Must be a color like #1a2b3c."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var lower = name!.ToLower();
        if (await db.Tags.AnyAsync(t => t.OwnerId == userId && t.Name.ToLower() == lower))
            return TypedResults.Conflict("A tag with this name already exists.");

        var tag = new Tag { OwnerId = userId, Name = name, Color = request.Color?.ToLowerInvariant() };
        db.Tags.Add(tag);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // The same name created in parallel: the unique index decided
            return TypedResults.Conflict("A tag with this name already exists.");
        }
        return TypedResults.Created($"/api/tags/{tag.Id}", ToDto(tag));
    }

    private static async Task<Results<NoContent, NotFound, UnauthorizedHttpResult>> DeleteTag(Guid id, HttpContext http, AppDbContext db)
    {
        if (UserId(http) is not { Length: > 0 } userId)
            return TypedResults.Unauthorized();

        var tag = await db.Tags.SingleOrDefaultAsync(t => t.Id == id && t.OwnerId == userId);
        if (tag is null)
            return TypedResults.NotFound();

        db.JobTags.RemoveRange(db.JobTags.Where(jt => jt.TagId == id));
        db.Tags.Remove(tag);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static TagDto ToDto(Tag tag) => new(tag.Id, tag.Name, tag.Color);

    private static async Task<Results<NoContent, ValidationProblem, UnauthorizedHttpResult>> SetJobTags(
        Guid id, SetJobTagsRequest request, HttpContext http, AppDbContext db)
    {
        if (UserId(http) is not { Length: > 0 } userId)
            return TypedResults.Unauthorized();

        var wanted = (request.TagIds ?? []).Distinct().ToList();
        if (wanted.Count > MaxTagsPerJob)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["tagIds"] = [$"At most {MaxTagsPerJob} tags per job."] });

        // Only the user's own tags: someone else's id is as good as a missing one
        var ownTagIds = await db.Tags.Where(t => t.OwnerId == userId && wanted.Contains(t.Id)).Select(t => t.Id).ToListAsync();
        if (ownTagIds.Count != wanted.Count)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["tagIds"] = ["Unknown tag."] });

        // Replace only the user's own assignments; other users' tags on a shared job stay
        var current = await db.JobTags
            .Where(jt => jt.AudioJobId == id && db.Tags.Any(t => t.Id == jt.TagId && t.OwnerId == userId))
            .ToListAsync();
        db.JobTags.RemoveRange(current.Where(jt => !wanted.Contains(jt.TagId)));
        foreach (var tagId in wanted.Where(w => current.All(jt => jt.TagId != w)))
            db.JobTags.Add(new JobTag { AudioJobId = id, TagId = tagId });

        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    // ================================================================ favorites

    private static async Task<Results<NoContent, UnauthorizedHttpResult>> AddFavorite(Guid id, HttpContext http, AppDbContext db)
    {
        if (UserId(http) is not { Length: > 0 } userId)
            return TypedResults.Unauthorized();

        if (!await db.JobFavorites.AnyAsync(f => f.AudioJobId == id && f.UserId == userId))
        {
            db.JobFavorites.Add(new JobFavorite { AudioJobId = id, UserId = userId });
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Marked in parallel by a second request: it is a favorite, which is what was asked for
            }
        }
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult>> RemoveFavorite(Guid id, HttpContext http, AppDbContext db)
    {
        if (UserId(http) is not { Length: > 0 } userId)
            return TypedResults.Unauthorized();

        var favorite = await db.JobFavorites.SingleOrDefaultAsync(f => f.AudioJobId == id && f.UserId == userId);
        if (favorite is not null)
        {
            db.JobFavorites.Remove(favorite);
            await db.SaveChangesAsync();
        }
        return TypedResults.NoContent();
    }

    // ================================================================ folders

    private static async Task<Results<Ok<List<FolderDto>>, UnauthorizedHttpResult>> GetFolders(HttpContext http, AppDbContext db)
    {
        if (UserId(http) is not { Length: > 0 } userId)
            return TypedResults.Unauthorized();

        var folders = await db.Folders.Where(f => f.OwnerId == userId).OrderBy(f => f.Name).ToListAsync();
        return TypedResults.Ok(folders.Select(f => new FolderDto(f.Id, f.Name, f.ParentId)).ToList());
    }

    private static async Task<Results<Created<FolderDto>, ValidationProblem, Conflict<string>, UnauthorizedHttpResult>> CreateFolder(
        SaveFolderRequest request, HttpContext http, AppDbContext db)
    {
        if (UserId(http) is not { Length: > 0 } userId)
            return TypedResults.Unauthorized();

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxFolderNameLength)
            return FolderValidation("name", $"Must be 1 to {MaxFolderNameLength} characters.");
        if (request.ParentId is { } parent && !await db.Folders.AnyAsync(f => f.Id == parent && f.OwnerId == userId))
            return FolderValidation("parentId", "Unknown folder.");

        if (await NameTakenAsync(db, userId, request.ParentId, name, exceptId: null))
            return TypedResults.Conflict("A folder with this name already exists here.");

        var folder = new Folder { OwnerId = userId, Name = name, ParentId = request.ParentId };
        db.Folders.Add(folder);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return TypedResults.Conflict("A folder with this name already exists here.");
        }
        return TypedResults.Created($"/api/folders/{folder.Id}", new FolderDto(folder.Id, folder.Name, folder.ParentId));
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<string>, UnauthorizedHttpResult>> UpdateFolder(
        Guid id, SaveFolderRequest request, HttpContext http, AppDbContext db)
    {
        if (UserId(http) is not { Length: > 0 } userId)
            return TypedResults.Unauthorized();

        var all = await db.Folders.Where(f => f.OwnerId == userId).ToListAsync();
        var folder = all.SingleOrDefault(f => f.Id == id);
        if (folder is null)
            return TypedResults.NotFound();

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxFolderNameLength)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["name"] = [$"Must be 1 to {MaxFolderNameLength} characters."] });
        if (request.ParentId is { } parent)
        {
            if (all.All(f => f.Id != parent))
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["parentId"] = ["Unknown folder."] });
            if (IsSelfOrDescendant(all, id, parent))
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["parentId"] = ["A folder cannot be moved into itself or one of its subfolders."] });
        }

        if (await NameTakenAsync(db, userId, request.ParentId, name, exceptId: id))
            return TypedResults.Conflict("A folder with this name already exists here.");

        folder.Name = name;
        folder.ParentId = request.ParentId;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return TypedResults.Conflict("A folder with this name already exists here.");
        }
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound, UnauthorizedHttpResult>> DeleteFolder(Guid id, HttpContext http, AppDbContext db)
    {
        if (UserId(http) is not { Length: > 0 } userId)
            return TypedResults.Unauthorized();

        var all = await db.Folders.Where(f => f.OwnerId == userId).ToListAsync();
        var folder = all.SingleOrDefault(f => f.Id == id);
        if (folder is null)
            return TypedResults.NotFound();

        // Subfolders move up one level (renamed if the name is taken there), jobs go to the top level
        var taken = all.Where(f => f.ParentId == folder.ParentId && f.Id != id).Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var child in all.Where(f => f.ParentId == id).OrderBy(f => f.Name))
        {
            child.ParentId = folder.ParentId;
            child.Name = UniqueName(child.Name, taken);
            taken.Add(child.Name);
        }
        foreach (var job in await db.AudioJobs.Where(j => j.FolderId == id).ToListAsync())
            job.FolderId = null;

        // The children have to be saved first: the database refuses to delete a folder that still has some
        await db.SaveChangesAsync();
        db.Folders.Remove(folder);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ValidationProblem, UnauthorizedHttpResult>> SetJobFolder(
        Guid id, SetJobFolderRequest request, HttpContext http, AppDbContext db)
    {
        if (UserId(http) is not { Length: > 0 } userId)
            return TypedResults.Unauthorized();

        if (request.FolderId is { } folderId && !await db.Folders.AnyAsync(f => f.Id == folderId && f.OwnerId == userId))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["folderId"] = ["Unknown folder."] });

        // The access filter already made sure the job exists and belongs to the caller
        var job = await db.AudioJobs.FindAsync(id);
        job!.FolderId = request.FolderId;
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static ValidationProblem FolderValidation(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static Task<bool> NameTakenAsync(AppDbContext db, string userId, Guid? parentId, string name, Guid? exceptId)
    {
        var lower = name.ToLower();
        return db.Folders.AnyAsync(f => f.OwnerId == userId && f.ParentId == parentId && f.Id != exceptId && f.Name.ToLower() == lower);
    }

    /// <summary>True if <paramref name="candidateParent"/> is the folder itself or sits anywhere below it.</summary>
    private static bool IsSelfOrDescendant(List<Folder> all, Guid folderId, Guid candidateParent)
    {
        var byId = all.ToDictionary(f => f.Id);
        var seen = new HashSet<Guid>();
        for (Guid? current = candidateParent; current is { } id && seen.Add(id); current = byId.GetValueOrDefault(id)?.ParentId)
        {
            if (id == folderId)
                return true;
        }
        return false;
    }

    private static string UniqueName(string name, HashSet<string> taken)
    {
        if (!taken.Contains(name))
            return name;
        for (var n = 2; ; n++)
        {
            var candidate = $"{name} ({n})";
            if (candidate.Length <= MaxFolderNameLength && !taken.Contains(candidate))
                return candidate;
        }
    }
}
