using System.Text;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Api.Storage;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Domain.Subtitles;
using AudioTranscription.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Api.Endpoints;

/// <summary>Timed segments, subtitle downloads and the audio of a job (S06), with speaker names when diarized (S11).</summary>
public static class TranscriptOutputEndpoints
{
    private const int MaxDownloadNameLength = 100;

    public static void MapTranscriptOutputEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/audio-jobs/{id:guid}");

        group.MapGet("/segments", GetSegments)
            .WithName("GetTranscriptSegments")
            .WithSummary("Timed transcript segments")
            .WithDescription("Timed segments of the raw transcript of a completed job");

        group.MapGet("/subtitles", GetSubtitles)
            .WithName("GetSubtitles")
            .WithSummary("Download subtitles (SRT/VTT)")
            .WithDescription("Download the transcript of a completed job as subtitles (format=srt or vtt)");

        group.MapGet("/audio", GetAudio)
            .WithName("GetAudio")
            .WithSummary("Stream the uploaded audio")
            .WithDescription("Stream the uploaded audio (supports HTTP range requests); 410 if the upload was already removed");
    }

    private static async Task<Results<Ok<List<TranscriptSegmentDto>>, NotFound<ProblemDetails>, Conflict<ProblemDetails>>> GetSegments(
        Guid id, AppDbContext dbContext)
    {
        var job = await dbContext.AudioJobs.FindAsync(id);
        if (job is null)
            return AudioJobEndpoints.JobNotFound(id);
        if (job.Status != AudioJobStatus.Completed)
            return NotCompleted(job.Status);

        var speakerNames = await SpeakerNaming.LoadResolvedNamesAsync(dbContext, id);
        var segments = await dbContext.TranscriptSegments
            .Where(s => s.AudioJobId == id)
            .OrderBy(s => s.Index)
            .ToListAsync();
        return TypedResults.Ok(segments
            .Select(s => new TranscriptSegmentDto(
                s.Index, s.StartMs, s.EndMs, s.Text,
                s.SpeakerIndex,
                s.SpeakerIndex is { } speakerIndex ? speakerNames.GetValueOrDefault(speakerIndex, SpeakerNaming.DefaultName(speakerIndex)) : null))
            .ToList());
    }

    private static async Task<Results<FileContentHttpResult, ValidationProblem, NotFound<ProblemDetails>, Conflict<ProblemDetails>>> GetSubtitles(
        Guid id, [FromQuery] string? format, AppDbContext dbContext)
    {
        var isSrt = string.Equals(format, "srt", StringComparison.OrdinalIgnoreCase);
        if (!isSrt && !string.Equals(format, "vtt", StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["format"] = ["Use format=srt or format=vtt."]
            });
        }

        var job = await dbContext.AudioJobs.FindAsync(id);
        if (job is null)
            return AudioJobEndpoints.JobNotFound(id);
        if (job.Status != AudioJobStatus.Completed)
            return NotCompleted(job.Status);

        var segments = await dbContext.TranscriptSegments.Where(s => s.AudioJobId == id).ToListAsync();
        var speakerNames = await SpeakerNaming.LoadResolvedNamesAsync(dbContext, id);
        var (content, contentType, extension) = isSrt
            ? (SubtitleFormatter.ToSrt(segments, speakerNames), "application/x-subrip; charset=utf-8", "srt")
            : (SubtitleFormatter.ToVtt(segments, speakerNames), "text/vtt; charset=utf-8", "vtt");

        return TypedResults.File(Encoding.UTF8.GetBytes(content), contentType, DownloadName(job.FileName, extension));
    }

    private static async Task<Results<PhysicalFileHttpResult, NotFound<ProblemDetails>, ProblemHttpResult>> GetAudio(
        Guid id, AppDbContext dbContext, ITempFileStore tempFileStore)
    {
        var job = await dbContext.AudioJobs.FindAsync(id);
        if (job is null)
            return AudioJobEndpoints.JobNotFound(id);

        // Uploads are deleted after a successful transcription unless Upload:DeleteAfterTranscription is off (D2)
        var filePath = Path.GetFullPath(tempFileStore.GetUploadPath(job.Id, job.FileName));
        if (!File.Exists(filePath))
        {
            return TypedResults.Problem(
                title: "Audio no longer available",
                detail: "The uploaded file of this job was already removed.",
                statusCode: StatusCodes.Status410Gone);
        }

        return TypedResults.PhysicalFile(filePath, job.ContentType, enableRangeProcessing: true);
    }

    private static Conflict<ProblemDetails> NotCompleted(AudioJobStatus status) =>
        AudioJobEndpoints.JobConflict($"Segments and subtitles exist only for completed jobs (current status: {status}).");

    /// <summary>
    /// File name for a download, derived from the uploaded file's name: only letters, digits, space,
    /// '-', '_' and '.' survive, so the name cannot break the Content-Disposition header or a path.
    /// </summary>
    internal static string DownloadName(string originalFileName, string extension)
    {
        var baseName = Path.GetFileNameWithoutExtension(originalFileName.Replace('\\', '/'));
        var safe = new string(baseName
                .Select(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.' ? c : '_')
                .Take(MaxDownloadNameLength)
                .ToArray())
            .Trim(' ', '.', '_');
        return $"{(safe.Length > 0 ? safe : "transcript")}.{extension}";
    }
}
