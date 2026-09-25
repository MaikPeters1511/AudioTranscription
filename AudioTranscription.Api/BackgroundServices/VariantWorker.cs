using AudioTranscription.Api.Dtos;
using AudioTranscription.Api.Hubs;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using AudioTranscription.Infrastructure.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Api.BackgroundServices;

/// <summary>
/// Processes on-demand transcript variants (S10) from <see cref="VariantQueue"/>: loads the job's raw
/// transcript, calls the configured <see cref="IVariantGenerator"/>, and stores the result. Runs
/// independently of <see cref="TranscriptionWorker"/> so Whisper jobs and LLM calls never block each other.
/// </summary>
public class VariantWorker(
    VariantQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<VariantWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("VariantWorker started, waiting for variant requests...");

        await foreach (var request in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessVariantAsync(request, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("VariantWorker stopping due to cancellation");
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error processing variant {VariantId}", request.VariantId);
            }
        }

        logger.LogInformation("VariantWorker stopped");
    }

    internal async Task ProcessVariantAsync(VariantJobRequest request, CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var generator = scope.ServiceProvider.GetRequiredService<IVariantGenerator>();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<TranscriptionHub>>();

        var variant = await dbContext.TranscriptVariants.FindAsync([request.VariantId], stoppingToken);
        if (variant is null)
        {
            logger.LogWarning("Variant {VariantId} not found in database, skipping", request.VariantId);
            return;
        }
        if (variant.Status != VariantStatus.Pending)
        {
            logger.LogInformation("Variant {VariantId} is {Status}, not Pending; skipping", request.VariantId, variant.Status);
            return;
        }

        var job = await dbContext.AudioJobs.FindAsync([variant.AudioJobId], stoppingToken);
        if (job?.RawTranscript is null)
        {
            logger.LogWarning("Job {JobId} of variant {VariantId} has no raw transcript, marking Failed", variant.AudioJobId, variant.Id);
            variant.Status = VariantStatus.Failed;
            variant.ErrorMessage = "Job hat kein Transkript.";
            variant.CompletedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(stoppingToken);
            await NotifyCompleted(hubContext, variant);
            return;
        }

        try
        {
            variant.Text = await generator.GenerateAsync(variant.Mode, job.RawTranscript, variant.TargetLanguage, stoppingToken);
            variant.Status = VariantStatus.Completed;
            variant.ErrorMessage = null;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw; // shutdown: leave the variant Pending so it is recovered and retried after restart
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Variant generation failed for {VariantId} (job {JobId}, mode {Mode})", variant.Id, variant.AudioJobId, variant.Mode);
            variant.Status = VariantStatus.Failed;
            variant.ErrorMessage = ex.Message;
        }

        variant.CompletedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(stoppingToken);
        await NotifyCompleted(hubContext, variant);
    }

    private static async Task NotifyCompleted(IHubContext<TranscriptionHub> hubContext, Domain.Entities.TranscriptVariant variant)
    {
        await hubContext.Clients.All.SendAsync("VariantCompleted",
            new VariantCompletedDto(variant.AudioJobId, variant.Id, variant.Mode, variant.Status));
    }
}
