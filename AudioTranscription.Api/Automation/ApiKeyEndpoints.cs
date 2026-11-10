using AudioTranscription.Api.Access;
using AudioTranscription.Domain.Automation;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Api.Automation;

public record CreateApiKeyRequest(string? Name, string[]? Scopes, DateTime? ExpiresAt);

/// <summary>An API key as listed: never the key or its hash, only what identifies it.</summary>
public record ApiKeyDto(Guid Id, string Name, string Prefix, string[] Scopes, DateTime CreatedAt, DateTime? ExpiresAt, DateTime? LastUsedAt);

/// <summary>The response of creating a key: the only time the key itself is shown.</summary>
public record CreatedApiKeyDto(Guid Id, string Name, string Prefix, string[] Scopes, DateTime CreatedAt, DateTime? ExpiresAt, string Key);

/// <summary>
/// Management of a user's API keys (S28, ADR 0009). Only the browser session manages keys: a key itself is refused here
/// (see <see cref="ApiKeyScopeMiddleware"/>), so a stolen key cannot create or extend keys.
/// </summary>
public static class ApiKeyEndpoints
{
    public const int MaxKeysPerUser = 20;
    public const int MaxLifetimeDays = 365;
    private const int MaxNameLength = 100;

    public static void MapApiKeyEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/api-keys").WithTags("API keys");

        group.MapGet("/", GetKeys)
            .WithName("GetApiKeys").WithSummary("The user's API keys")
            .WithDescription("Name, prefix, scopes and dates of the signed-in user's keys; the key itself is never returned again");
        group.MapPost("/", CreateKey)
            .WithName("CreateApiKey").WithSummary("Create an API key")
            .WithDescription($"Returns the key exactly once. Scopes: {string.Join(", ", ApiKeyScopes.All)}. Optional expiry of at most {MaxLifetimeDays} days; at most {MaxKeysPerUser} keys per user. Send the key as 'Authorization: Bearer <key>' or header 'X-Api-Key'. A key reaches only /api/audio-jobs, /api/uploads and /api/search.");
        group.MapDelete("/{id:guid}", DeleteKey)
            .WithName("DeleteApiKey").WithSummary("Revoke an API key").WithDescription("The key stops working immediately");
    }

    private static async Task<Results<Ok<List<ApiKeyDto>>, UnauthorizedHttpResult>> GetKeys(HttpContext http, AppDbContext db)
    {
        if (JobAccessService.GetUserId(http.User) is not { Length: > 0 } userId)
            return TypedResults.Unauthorized();

        var keys = await db.ApiKeys.AsNoTracking().Where(k => k.OwnerId == userId).OrderByDescending(k => k.CreatedAtUtc).ToListAsync();
        return TypedResults.Ok(keys.Select(ToDto).ToList());
    }

    private static async Task<Results<Created<CreatedApiKeyDto>, ValidationProblem, Conflict<string>, UnauthorizedHttpResult>> CreateKey(
        CreateApiKeyRequest request, HttpContext http, AppDbContext db, TimeProvider time)
    {
        if (JobAccessService.GetUserId(http.User) is not { Length: > 0 } userId)
            return TypedResults.Unauthorized();

        var now = time.GetUtcNow().UtcDateTime;
        var errors = new Dictionary<string, string[]>();
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > MaxNameLength)
            errors["name"] = [$"Must be 1 to {MaxNameLength} characters."];
        var scopes = request.Scopes ?? [];
        if (!ApiKeyScopes.AreValid(scopes))
            errors["scopes"] = [$"Choose at least one of: {string.Join(", ", ApiKeyScopes.All)} (each once)."];
        DateTime? expiresAt = request.ExpiresAt is { } requested ? requested.ToUniversalTime() : null;
        if (expiresAt is { } expiry && (expiry <= now || expiry > now.AddDays(MaxLifetimeDays)))
            errors["expiresAt"] = [$"Must be in the future and at most {MaxLifetimeDays} days from now."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        if (await db.ApiKeys.CountAsync(k => k.OwnerId == userId) >= MaxKeysPerUser)
            return TypedResults.Conflict($"At most {MaxKeysPerUser} API keys are allowed. Delete one first.");

        var token = ApiKeyToken.Generate();
        var entity = new ApiKey
        {
            OwnerId = userId,
            Name = name,
            Prefix = token.Prefix,
            KeyHash = token.Hash,
            Scopes = string.Join(',', scopes),
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAt,
        };
        db.ApiKeys.Add(entity);
        await db.SaveChangesAsync();

        return TypedResults.Created($"/api/api-keys/{entity.Id}",
            new CreatedApiKeyDto(entity.Id, entity.Name, entity.Prefix, [.. scopes], entity.CreatedAtUtc, entity.ExpiresAtUtc, token.Key));
    }

    private static async Task<Results<NoContent, NotFound, UnauthorizedHttpResult>> DeleteKey(Guid id, HttpContext http, AppDbContext db)
    {
        if (JobAccessService.GetUserId(http.User) is not { Length: > 0 } userId)
            return TypedResults.Unauthorized();

        var key = await db.ApiKeys.SingleOrDefaultAsync(k => k.Id == id && k.OwnerId == userId);
        if (key is null)
            return TypedResults.NotFound();

        db.ApiKeys.Remove(key);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static ApiKeyDto ToDto(ApiKey key) =>
        new(key.Id, key.Name, key.Prefix, key.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries), key.CreatedAtUtc, key.ExpiresAtUtc, key.LastUsedAtUtc);
}
