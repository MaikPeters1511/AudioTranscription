using System.Diagnostics;
using AudioTranscription.Api.Access;
using AudioTranscription.Api.Telemetry;
using AudioTranscription.Infrastructure.Telemetry;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Domain.Templates;
using AudioTranscription.Infrastructure.Data;
using AudioTranscription.Infrastructure.Services;
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
        var notifier = scope.ServiceProvider.GetRequiredService<IJobNotifier>();

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
            await NotifyCompleted(notifier, variant);
            return;
        }

        using var activity = AppTelemetry.Source.StartActivity("postprocessing");
        activity?.SetTag("postprocessing.mode", variant.Mode.ToString());
        var stopwatch = Stopwatch.StartNew();
        try
        {
            variant.Text = variant.Mode == PostProcessingMode.Template
                ? await GenerateFromTemplateAsync(scope.ServiceProvider, dbContext, variant, job, stoppingToken)
                : await generator.GenerateAsync(variant.Mode, job.RawTranscript, variant.TargetLanguage, stoppingToken);
            variant.Status = VariantStatus.Completed;
            variant.ErrorMessage = null;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw; // shutdown: leave the variant Pending so it is recovered and retried after restart
        }
        catch (Exception ex) when (variant.Mode == PostProcessingMode.Template)
        {
            // Template results are checked against a schema; users get a fixed message, the details go to the log
            logger.LogError(ex, "Template variant generation failed for {VariantId} (job {JobId})", variant.Id, variant.AudioJobId);
            variant.Status = VariantStatus.Failed;
            variant.ErrorMessage = ex switch
            {
                TemplateOutputException => InvalidOutputMessage,
                InvalidOperationException { Message: TemplateGoneMessage } => TemplateGoneMessage,
                _ => GenerationFailureMessage,
            };
            activity?.SetStatus(ActivityStatusCode.Error);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Variant generation failed for {VariantId} (job {JobId}, mode {Mode})", variant.Id, variant.AudioJobId, variant.Mode);
            variant.Status = VariantStatus.Failed;
            variant.ErrorMessage = ex.Message;
            activity?.SetStatus(ActivityStatusCode.Error);
        }

        scope.ServiceProvider.GetService<TranscriptionMetrics>()?.PostProcessingFinished(
            variant.Mode.ToString(),
            variant.Status == VariantStatus.Completed ? TranscriptionOutcome.Completed : TranscriptionOutcome.Failed,
            stopwatch.Elapsed);

        variant.CompletedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(stoppingToken);
        await NotifyCompleted(notifier, variant);
    }

    public const string InvalidOutputMessage = "Das Sprachmodell hat kein gültiges Ergebnis für diese Vorlage geliefert.";
    public const string TemplateGoneMessage = "Die Vorlage existiert nicht mehr.";
    public const string GenerationFailureMessage = "Das Ergebnis konnte nicht erzeugt werden. Ist das Sprachmodell erreichbar?";

    /// <summary>
    /// Runs a template (S26). Named speakers (a name was chosen, not the default "Sprecher N") are put in front of what
    /// they said and are the only people the result may name as owners.
    /// </summary>
    private static async Task<string> GenerateFromTemplateAsync(
        IServiceProvider services, AppDbContext dbContext, Domain.Entities.TranscriptVariant variant, Domain.Entities.AudioJob job, CancellationToken cancellationToken)
    {
        var generator = services.GetRequiredService<ITemplateVariantGenerator>();
        var template = variant.TemplateId is { } templateId
            ? await dbContext.PostprocessingTemplates.FindAsync([templateId], cancellationToken)
            : null;
        if (template is null)
            throw new InvalidOperationException(TemplateGoneMessage);

        var speakers = await dbContext.JobSpeakers
            .Where(s => s.AudioJobId == job.Id && s.DisplayName != null && s.DisplayName != "")
            .ToDictionaryAsync(s => s.Index, s => s.DisplayName!, cancellationToken);
        var transcript = job.RawTranscript!;
        if (speakers.Count > 0)
        {
            var segments = await dbContext.TranscriptSegments.Where(s => s.AudioJobId == job.Id).OrderBy(s => s.Index).ToListAsync(cancellationToken);
            if (segments.Count > 0)
                transcript = SpeakerTranscript.Build(segments, speakers);
        }

        return await generator.GenerateAsync(template, transcript, speakers.Values.Distinct().ToList(), cancellationToken);
    }

    private static async Task NotifyCompleted(IJobNotifier notifier, Domain.Entities.TranscriptVariant variant)
    {
        await notifier.NotifyAsync(variant.AudioJobId, "VariantCompleted",
            new VariantCompletedDto(variant.AudioJobId, variant.Id, variant.Mode, variant.Status));
    }
}
