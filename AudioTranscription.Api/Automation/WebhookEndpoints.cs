using AudioTranscription.Api.Access;
using AudioTranscription.Domain.Automation;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudioTranscription.Api.Automation;

/// <summary>Create and update. <c>IsActive</c> is optional: active on create, unchanged on update.</summary>
public record SaveWebhookRequest(string? Url, string[]? Events, bool? IsActive);

public record WebhookDto(Guid Id, string Url, string[] Events, bool IsActive, DateTime CreatedAt);

/// <summary>The response of creating a webhook: the only time the signing secret is shown.</summary>
public record CreatedWebhookDto(Guid Id, string Url, string[] Events, bool IsActive, DateTime CreatedAt, string Secret);

public record WebhookDeliveryDto(
    Guid Id, string Event, Guid? JobId, string Status, int Attempts, DateTime NextAttemptAt,
    int? LastStatusCode, string? LastError, DateTime CreatedAt, DateTime? CompletedAt);

/// <summary>A user's webhooks and their delivery log (S28, ADR 0009). Webhooks are private to their owner.</summary>
public static class WebhookEndpoints
{
    public const int MaxWebhooksPerUser = 10;
    public const string TestRateLimitPolicy = "webhook-test";
    private const int HistoryLimit = 100;

    public static void MapWebhookEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/webhooks").WithTags("Webhooks");

        group.MapGet("/", GetWebhooks)
            .WithName("GetWebhooks").WithSummary("The user's webhooks").WithDescription("URL, events and state; the secret is never returned again");
        group.MapPost("/", CreateWebhook)
            .WithName("CreateWebhook").WithSummary("Create a webhook")
            .WithDescription($"Events: {string.Join(", ", WebhookEvents.Subscribable)}. The URL must be https and public (no internal addresses, no redirects are followed). Returns the signing secret exactly once. Each request carries X-Webhook-Id, X-Webhook-Event, X-Webhook-Timestamp and X-Webhook-Signature (v1=hex HMAC-SHA256 of '<timestamp>.<body>'). At most {MaxWebhooksPerUser} per user.");
        group.MapPut("/{id:guid}", UpdateWebhook)
            .WithName("UpdateWebhook").WithSummary("Change a webhook").WithDescription("Replaces URL and events; the secret stays the same");
        group.MapDelete("/{id:guid}", DeleteWebhook)
            .WithName("DeleteWebhook").WithSummary("Delete a webhook").WithDescription("Also removes its delivery log");
        group.MapPost("/{id:guid}/test", TestWebhook)
            .WithName("TestWebhook").WithSummary("Send a test event")
            .WithDescription("Sends a signed 'webhook.test' immediately with a single attempt and returns the delivery; 10 per minute and user")
            .RequireRateLimiting(TestRateLimitPolicy);
        group.MapGet("/{id:guid}/deliveries", GetDeliveries)
            .WithName("GetWebhookDeliveries").WithSummary("Delivery log of a webhook").WithDescription($"The latest {HistoryLimit} deliveries, newest first, with status and attempts");
    }

    private static async Task<Results<Ok<List<WebhookDto>>, UnauthorizedHttpResult>> GetWebhooks(HttpContext http, AppDbContext db)
    {
        if (JobAccessService.GetUserId(http.User) is not { Length: > 0 } userId)
            return TypedResults.Unauthorized();

        var hooks = await db.Webhooks.AsNoTracking().Where(w => w.OwnerId == userId).OrderBy(w => w.CreatedAtUtc).ToListAsync();
        return TypedResults.Ok(hooks.Select(ToDto).ToList());
    }

    private static async Task<Results<Created<CreatedWebhookDto>, ValidationProblem, Conflict<string>, UnauthorizedHttpResult>> CreateWebhook(
        SaveWebhookRequest request, HttpContext http, AppDbContext db, IOptions<WebhookOptions> options,
        WebhookSecretProtector secrets, TimeProvider time)
    {
        if (JobAccessService.GetUserId(http.User) is not { Length: > 0 } userId)
            return TypedResults.Unauthorized();
        if (Validate(request, options.Value) is { } errors)
            return TypedResults.ValidationProblem(errors);
        if (await db.Webhooks.CountAsync(w => w.OwnerId == userId) >= MaxWebhooksPerUser)
            return TypedResults.Conflict($"At most {MaxWebhooksPerUser} webhooks are allowed. Delete one first.");

        var secret = WebhookSigner.GenerateSecret();
        var hook = new Webhook
        {
            OwnerId = userId,
            Url = request.Url!.Trim(),
            ProtectedSecret = secrets.Protect(secret),
            Events = string.Join(',', request.Events!),
            IsActive = request.IsActive ?? true,
            CreatedAtUtc = time.GetUtcNow().UtcDateTime,
        };
        db.Webhooks.Add(hook);
        await db.SaveChangesAsync();

        return TypedResults.Created($"/api/webhooks/{hook.Id}",
            new CreatedWebhookDto(hook.Id, hook.Url, [.. request.Events!], hook.IsActive, hook.CreatedAtUtc, secret));
    }

    private static async Task<Results<Ok<WebhookDto>, ValidationProblem, NotFound, UnauthorizedHttpResult>> UpdateWebhook(
        Guid id, SaveWebhookRequest request, HttpContext http, AppDbContext db, IOptions<WebhookOptions> options)
    {
        if (JobAccessService.GetUserId(http.User) is not { Length: > 0 } userId)
            return TypedResults.Unauthorized();
        if (Validate(request, options.Value) is { } errors)
            return TypedResults.ValidationProblem(errors);

        var hook = await db.Webhooks.SingleOrDefaultAsync(w => w.Id == id && w.OwnerId == userId);
        if (hook is null)
            return TypedResults.NotFound();

        hook.Url = request.Url!.Trim();
        hook.Events = string.Join(',', request.Events!);
        hook.IsActive = request.IsActive ?? hook.IsActive;
        await db.SaveChangesAsync();
        return TypedResults.Ok(ToDto(hook));
    }

    private static async Task<Results<NoContent, NotFound, UnauthorizedHttpResult>> DeleteWebhook(Guid id, HttpContext http, AppDbContext db)
    {
        if (JobAccessService.GetUserId(http.User) is not { Length: > 0 } userId)
            return TypedResults.Unauthorized();

        var hook = await db.Webhooks.SingleOrDefaultAsync(w => w.Id == id && w.OwnerId == userId);
        if (hook is null)
            return TypedResults.NotFound();

        db.WebhookDeliveries.RemoveRange(await db.WebhookDeliveries.Where(d => d.WebhookId == id).ToListAsync());
        db.Webhooks.Remove(hook);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<WebhookDeliveryDto>, NotFound, UnauthorizedHttpResult>> TestWebhook(
        Guid id, HttpContext http, AppDbContext db, WebhookDeliveryProcessor processor, TimeProvider time, CancellationToken cancellationToken)
    {
        if (JobAccessService.GetUserId(http.User) is not { Length: > 0 } userId)
            return TypedResults.Unauthorized();
        if (!await db.Webhooks.AnyAsync(w => w.Id == id && w.OwnerId == userId, cancellationToken))
            return TypedResults.NotFound();

        var now = time.GetUtcNow().UtcDateTime;
        var deliveryId = Guid.NewGuid();
        var delivery = new WebhookDelivery
        {
            Id = deliveryId,
            WebhookId = id,
            Event = WebhookEvents.Test,
            Payload = WebhookPayload.ForTest(deliveryId, now),
            NextAttemptAtUtc = now,
            CreatedAtUtc = now,
        };
        db.WebhookDeliveries.Add(delivery);
        await db.SaveChangesAsync(cancellationToken);

        await processor.AttemptAsync(delivery, retry: false, cancellationToken);
        return TypedResults.Ok(ToDto(delivery));
    }

    private static async Task<Results<Ok<List<WebhookDeliveryDto>>, NotFound, UnauthorizedHttpResult>> GetDeliveries(
        Guid id, HttpContext http, AppDbContext db)
    {
        if (JobAccessService.GetUserId(http.User) is not { Length: > 0 } userId)
            return TypedResults.Unauthorized();
        if (!await db.Webhooks.AnyAsync(w => w.Id == id && w.OwnerId == userId))
            return TypedResults.NotFound();

        var deliveries = await db.WebhookDeliveries.AsNoTracking()
            .Where(d => d.WebhookId == id)
            .OrderByDescending(d => d.CreatedAtUtc).ThenByDescending(d => d.Id)
            .Take(HistoryLimit)
            .ToListAsync();
        return TypedResults.Ok(deliveries.Select(ToDto).ToList());
    }

    private static Dictionary<string, string[]>? Validate(SaveWebhookRequest request, WebhookOptions options)
    {
        var errors = new Dictionary<string, string[]>();
        if (WebhookTargetPolicy.Validate(request.Url, options.AllowHttp, options.AllowPrivateTargets) is { } urlError)
            errors["url"] = [urlError];
        if (!WebhookEvents.AreValid(request.Events ?? []))
            errors["events"] = [$"Choose at least one of: {string.Join(", ", WebhookEvents.Subscribable)} (each once)."];
        return errors.Count > 0 ? errors : null;
    }

    private static WebhookDto ToDto(Webhook hook) =>
        new(hook.Id, hook.Url, hook.Events.Split(',', StringSplitOptions.RemoveEmptyEntries), hook.IsActive, hook.CreatedAtUtc);

    private static WebhookDeliveryDto ToDto(WebhookDelivery d) =>
        new(d.Id, d.Event, d.JobId, d.Status.ToString().ToLowerInvariant(), d.Attempts, d.NextAttemptAtUtc,
            d.LastStatusCode, d.LastError, d.CreatedAtUtc, d.CompletedAtUtc);
}
