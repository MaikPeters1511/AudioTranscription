using AudioTranscription.Api.BackgroundServices;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using AudioTranscription.Infrastructure.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudioTranscription.Api.Endpoints;

/// <summary>On-demand transcript variants (S10): cleanup, summary, bullet points, action items, translation.</summary>
public static class VariantEndpoints
{
    public static void MapVariantEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/audio-jobs/{id:guid}");

        group.MapPost("/variants", CreateVariant)
            .WithName("CreateTranscriptVariant")
            .WithDescription("Generate (or regenerate) a transcript variant; requires a completed job and a configured LLM");

        group.MapGet("/variants", GetVariants)
            .WithName("GetTranscriptVariants")
            .WithDescription("List the transcript variants generated for a job");
    }

    private static async Task<Results<Accepted<TranscriptVariantDto>, ValidationProblem, NotFound<ProblemDetails>, Conflict<ProblemDetails>, StatusCodeHttpResult>> CreateVariant(
        Guid id,
        CreateVariantRequest request,
        AppDbContext dbContext,
        IServiceProvider services,
        IOptions<WhisperOptions> whisperOptions)
    {
        if (!Enum.IsDefined(request.Mode))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["mode"] = [$"'{request.Mode}' is not a known post-processing mode."]
            });
        }

        // Non-translate modes ignore whatever target language is sent; translate requires a supported one
        string? targetLanguage = null;
        if (request.Mode == PostProcessingMode.Translate)
        {
            targetLanguage = whisperOptions.Value.SupportedLanguages
                .FirstOrDefault(l => l.Equals(request.TargetLanguage?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (targetLanguage is null)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["targetLanguage"] = [$"Use one of: {string.Join(", ", whisperOptions.Value.SupportedLanguages)}."]
                });
            }
        }

        var job = await dbContext.AudioJobs.FindAsync(id);
        if (job is null)
            return AudioJobEndpoints.JobNotFound(id);
        if (job.Status != AudioJobStatus.Completed)
            return AudioJobEndpoints.JobConflict($"Variants can only be generated for completed jobs (current status: {job.Status}).");

        var generator = services.GetService<IVariantGenerator>();
        var variantQueue = services.GetService<VariantQueue>();
        if (generator is null || variantQueue is null)
        {
            return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        // "A rerun overwrites it": reuse the existing row for this (job, mode, language) instead of adding a second one
        var variant = await dbContext.TranscriptVariants.SingleOrDefaultAsync(
            v => v.AudioJobId == id && v.Mode == request.Mode && v.TargetLanguage == targetLanguage);
        if (variant is null)
        {
            variant = new TranscriptVariant { AudioJobId = id, Mode = request.Mode, TargetLanguage = targetLanguage };
            dbContext.TranscriptVariants.Add(variant);
        }
        else
        {
            variant.Status = VariantStatus.Pending;
            variant.Text = null;
            variant.ErrorMessage = null;
            variant.CreatedAtUtc = DateTime.UtcNow;
            variant.CompletedAtUtc = null;
        }
        await dbContext.SaveChangesAsync();
        await variantQueue.EnqueueAsync(new VariantJobRequest(variant.Id));

        return TypedResults.Accepted($"/api/audio-jobs/{id}/variants", ToDto(variant));
    }

    private static async Task<Results<Ok<List<TranscriptVariantDto>>, NotFound<ProblemDetails>>> GetVariants(
        Guid id, AppDbContext dbContext)
    {
        if (!await dbContext.AudioJobs.AnyAsync(j => j.Id == id))
            return AudioJobEndpoints.JobNotFound(id);

        var variants = await dbContext.TranscriptVariants
            .Where(v => v.AudioJobId == id)
            .OrderBy(v => v.CreatedAtUtc)
            .Select(v => new TranscriptVariantDto(v.Id, v.Mode, v.TargetLanguage, v.Status, v.Text, v.ErrorMessage, v.CreatedAtUtc, v.CompletedAtUtc))
            .ToListAsync();
        return TypedResults.Ok(variants);
    }

    private static TranscriptVariantDto ToDto(TranscriptVariant v) =>
        new(v.Id, v.Mode, v.TargetLanguage, v.Status, v.Text, v.ErrorMessage, v.CreatedAtUtc, v.CompletedAtUtc);
}
