using System.Text.Json;
using AudioTranscription.Domain.Automation;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudioTranscription.Api.Automation;

/// <summary>Turns a finished job into deliveries for the owner's webhooks (S28).</summary>
public interface IWebhookDispatcher
{
    /// <param name="eventName">One of <see cref="WebhookEvents.Subscribable"/>.</param>
    Task EnqueueAsync(AudioJob job, string eventName, CancellationToken cancellationToken);
}

/// <summary>
/// Writes one pending delivery per matching webhook (the outbox). Nothing is sent here, so a slow receiver can never
/// delay a job; <see cref="WebhookDeliveryService"/> sends.
/// </summary>
public class WebhookDispatcher(AppDbContext db, TimeProvider time) : IWebhookDispatcher
{
    public async Task EnqueueAsync(AudioJob job, string eventName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(job.OwnerId))
            return;

        var hooks = (await db.Webhooks.Where(w => w.OwnerId == job.OwnerId && w.IsActive).ToListAsync(cancellationToken))
            .Where(w => w.Events.Split(',').Contains(eventName))
            .ToList();
        if (hooks.Count == 0)
            return;

        var now = time.GetUtcNow().UtcDateTime;
        foreach (var hook in hooks)
        {
            var id = Guid.NewGuid();
            db.WebhookDeliveries.Add(new WebhookDelivery
            {
                Id = id,
                WebhookId = hook.Id,
                Event = eventName,
                JobId = job.Id,
                Payload = WebhookPayload.ForJob(id, eventName, job, now),
                NextAttemptAtUtc = now,
                CreatedAtUtc = now,
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>The JSON a receiver gets. Metadata only: no transcript, no error text, no file contents (ADR 0009).</summary>
public static class WebhookPayload
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string ForJob(Guid deliveryId, string eventName, AudioJob job, DateTime now) =>
        JsonSerializer.Serialize(new
        {
            @event = eventName,
            deliveryId,
            createdAt = now,
            data = new
            {
                jobId = job.Id,
                fileName = job.FileName,
                status = job.Status.ToString(),
                language = job.Language,
                durationSeconds = job.DurationSeconds,
                completedAt = job.CompletedAtUtc,
            },
        }, Json);

    public static string ForTest(Guid deliveryId, DateTime now) =>
        JsonSerializer.Serialize(new { @event = WebhookEvents.Test, deliveryId, createdAt = now, data = new { message = "Test delivery from AudioTranscription." } }, Json);
}

/// <summary>Sends due deliveries, books the outcome and plans the next attempt (ADR 0009).</summary>
public class WebhookDeliveryProcessor(
    AppDbContext db,
    WebhookSender sender,
    WebhookSecretProtector secrets,
    IOptions<WebhookOptions> options,
    TimeProvider time,
    ILogger<WebhookDeliveryProcessor> logger)
{
    private const int BatchSize = 50;

    /// <summary>Sends every pending delivery whose time has come.</summary>
    public async Task ProcessDueAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var due = await db.WebhookDeliveries
            .Where(d => d.Status == WebhookDeliveryStatus.Pending && d.NextAttemptAtUtc <= now)
            .OrderBy(d => d.NextAttemptAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var delivery in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await AttemptAsync(delivery, retry: true, cancellationToken);
        }
    }

    /// <summary>One attempt. With <paramref name="retry"/> false (the test button) a failure is final.</summary>
    public async Task<WebhookDelivery> AttemptAsync(WebhookDelivery delivery, bool retry, CancellationToken cancellationToken)
    {
        var hook = await db.Webhooks.AsNoTracking().SingleOrDefaultAsync(w => w.Id == delivery.WebhookId, cancellationToken);
        if (hook is null || (!hook.IsActive && delivery.Event != WebhookEvents.Test))
        {
            Finish(delivery, WebhookDeliveryStatus.Failed, "webhook disabled");
            await db.SaveChangesAsync(cancellationToken);
            return delivery;
        }

        delivery.Attempts++;
        WebhookSendResult result;
        try
        {
            result = await sender.SendAsync(hook, secrets.Unprotect(hook.ProtectedSecret), delivery, cancellationToken);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // The Data Protection keys changed (not persisted?): retrying cannot help, the operator has to act
            logger.LogError("Webhook delivery {DeliveryId}: the signing secret cannot be decrypted; check the Data Protection key storage", delivery.Id);
            result = new WebhookSendResult(null, "secret unreadable", Permanent: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Only the type is logged: the message may carry the target address
            logger.LogWarning("Webhook delivery {DeliveryId} failed unexpectedly ({ExceptionType})", delivery.Id, ex.GetType().Name);
            result = new WebhookSendResult(null, "error", Permanent: false);
        }

        delivery.LastStatusCode = result.StatusCode;
        if (result.Success)
        {
            Finish(delivery, WebhookDeliveryStatus.Succeeded, null);
        }
        else
        {
            var delay = retry && !result.Permanent
                ? WebhookRetryPolicy.NextDelay(delivery.Attempts, options.Value.RetryDelays)
                : null;
            if (delay is { } wait)
            {
                delivery.LastError = result.Error;
                delivery.NextAttemptAtUtc = time.GetUtcNow().UtcDateTime + wait;
            }
            else
            {
                Finish(delivery, WebhookDeliveryStatus.Failed, result.Error);
            }
        }

        logger.LogInformation("Webhook delivery {DeliveryId} attempt {Attempt}: {Status}", delivery.Id, delivery.Attempts, delivery.Status);
        await db.SaveChangesAsync(cancellationToken);
        return delivery;
    }

    /// <summary>Removes deliveries older than the retention period.</summary>
    public async Task PruneAsync(CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow().UtcDateTime.AddDays(-options.Value.RetentionDays);
        var old = await db.WebhookDeliveries.Where(d => d.CreatedAtUtc < cutoff).ToListAsync(cancellationToken);
        if (old.Count == 0)
            return;
        db.WebhookDeliveries.RemoveRange(old);
        await db.SaveChangesAsync(cancellationToken);
    }

    private void Finish(WebhookDelivery delivery, WebhookDeliveryStatus status, string? error)
    {
        delivery.Status = status;
        delivery.LastError = error;
        delivery.CompletedAtUtc = time.GetUtcNow().UtcDateTime;
    }
}

/// <summary>Polls the outbox. Deliveries live in the database, so a restart loses none.</summary>
public class WebhookDeliveryService(IServiceScopeFactory scopes, IOptions<WebhookOptions> options, TimeProvider time, ILogger<WebhookDeliveryService> logger)
    : BackgroundService
{
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.PollIntervalSeconds));
        var nextPrune = time.GetUtcNow();
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<WebhookDeliveryProcessor>();
                await processor.ProcessDueAsync(stoppingToken);
                if (time.GetUtcNow() >= nextPrune)
                {
                    await processor.PruneAsync(stoppingToken);
                    nextPrune = time.GetUtcNow() + PruneInterval;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Webhook delivery run failed; trying again at the next interval");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
