using AudioTranscription.Api.Access;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Domain.Export;
using AudioTranscription.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Api.Endpoints;

/// <summary>Transcript export as DOCX, Markdown, JSON or PDF (S21), for the raw transcript or one of its variants (S10).</summary>
public static class ExportEndpoints
{
    public static void MapExportEndpoints(this WebApplication app)
    {
        app.MapGroup("/api/audio-jobs/{id:guid}")
            .RequireJobAccess()
            .MapGet("/export", Export)
            .WithName("ExportTranscript")
            .WithSummary("Export the transcript (DOCX, Markdown, JSON, PDF)")
            .WithDescription(
                "Download the transcript of a completed job. format=docx|md|json|pdf; variant=<variant id> exports a completed " +
                "post-processed variant instead of the raw transcript; timestamps and speakers (default true) switch those parts off. " +
                "JSON carries a schemaVersion.")
            .Produces<FileContentHttpResult>(StatusCodes.Status200OK);
    }

    private static async Task<Results<FileContentHttpResult, ValidationProblem, NotFound<ProblemDetails>, Conflict<ProblemDetails>>> Export(
        Guid id,
        [FromQuery] string? format,
        [FromQuery] Guid? variant,
        [FromQuery] bool? timestamps,
        [FromQuery] bool? speakers,
        AppDbContext dbContext,
        IEnumerable<ITranscriptExporter> exporters)
    {
        var exporter = exporters.FirstOrDefault(e => string.Equals(e.Format, format, StringComparison.OrdinalIgnoreCase));
        if (exporter is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["format"] = [$"Use one of: {string.Join(", ", exporters.Select(e => e.Format))}."]
            });
        }

        var job = await dbContext.AudioJobs.FindAsync(id);
        if (job is null)
            return AudioJobEndpoints.JobNotFound(id);
        if (job.Status != AudioJobStatus.Completed)
            return AudioJobEndpoints.JobConflict($"Only completed jobs can be exported (current status: {job.Status}).");

        var title = Path.GetFileNameWithoutExtension(job.FileName.Replace('\\', '/'));
        if (title.Length == 0)
            title = "transcript";

        ExportDocument document;
        string? suffix = null;
        if (variant is { } variantId)
        {
            var found = await dbContext.TranscriptVariants.FirstOrDefaultAsync(v => v.Id == variantId && v.AudioJobId == id);
            if (found is null)
            {
                return TypedResults.NotFound(new ProblemDetails
                {
                    Title = "Variant not found",
                    Detail = $"Job {id} has no variant {variantId}.",
                    Status = StatusCodes.Status404NotFound,
                });
            }
            if (found.Status != VariantStatus.Completed || string.IsNullOrWhiteSpace(found.Text))
                return AudioJobEndpoints.JobConflict("Only completed variants can be exported.");

            var label = found.Mode == PostProcessingMode.Translate && found.TargetLanguage is not null
                ? $"{found.Mode}-{found.TargetLanguage}"
                : found.Mode.ToString();
            document = ExportDocumentBuilder.FromVariantText(title, job.CreatedAtUtc, label, found.Text);
            suffix = label.ToLowerInvariant();
        }
        else
        {
            var segments = await dbContext.TranscriptSegments.Where(s => s.AudioJobId == id).ToListAsync();
            var speakerNames = await SpeakerNaming.LoadResolvedNamesAsync(dbContext, id);
            document = ExportDocumentBuilder.FromSegments(
                title, job.CreatedAtUtc, segments, speakerNames,
                new ExportOptions(timestamps ?? true, speakers ?? true));
        }

        var fileName = TranscriptOutputEndpoints.DownloadName(
            suffix is null ? job.FileName : $"{title}-{suffix}.x", exporter.Extension);
        return TypedResults.File(exporter.Export(document), exporter.ContentType, fileName);
    }
}
