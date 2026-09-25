using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Api.BackgroundServices;

/// <summary>
/// The variant queue lives in memory, so a variant still Pending when the API stops would otherwise
/// never run. At startup this service re-enqueues every Pending variant, analogous to <see cref="JobRecoveryService"/>.
/// </summary>
public class VariantRecoveryService(
    IServiceScopeFactory scopeFactory,
    VariantQueue queue,
    ILogger<VariantRecoveryService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pending = await dbContext.TranscriptVariants
            .Where(v => v.Status == VariantStatus.Pending)
            .OrderBy(v => v.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        foreach (var variant in pending)
            await queue.EnqueueAsync(new VariantJobRequest(variant.Id), cancellationToken);

        if (pending.Count > 0)
            logger.LogInformation("Recovered {Count} pending variant(s) after restart", pending.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
