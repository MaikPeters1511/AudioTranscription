using AudioTranscription.Api.BackgroundServices;
using AudioTranscription.Api.Configuration;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Api.Hubs;
using AudioTranscription.Api.Storage;
using AudioTranscription.Api.Uploads;
using AudioTranscription.Api.Validation;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using AudioTranscription.Infrastructure.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudioTranscription.Api.Endpoints;

public static class AudioJobEndpoints
{
    public static void MapAudioJobEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/audio-jobs");

        group.MapPost("/", UploadAudioJob)
            .DisableAntiforgery()
            .WithName("UploadAudioJob")
            .WithDescription("Upload an audio file for transcription");

        group.MapGet("/", GetAudioJobs)
            .WithName("GetAudioJobs")
            .WithDescription("Get paginated list of audio jobs");

        group.MapGet("/{id:guid}", GetAudioJob)
            .WithName("GetAudioJob")
            .WithDescription("Get a specific audio job by ID");

        group.MapDelete("/{id:guid}", DeleteAudioJob)
            .WithName("DeleteAudioJob")
            .WithDescription("Delete a job with its transcript and uploaded file; cancels it first if it is running");

        group.MapPost("/{id:guid}/cancel", CancelAudioJob)
            .WithName("CancelAudioJob")
            .WithDescription("Cancel a pending or running job");

        group.MapPost("/{id:guid}/retry", RetryAudioJob)
            .WithName("RetryAudioJob")
            .WithDescription("Re-queue a failed or cancelled job using its kept upload");
    }

    private static async Task<Results<NoContent, NotFound<ProblemDetails>>> DeleteAudioJob(
        Guid id,
        AppDbContext dbContext,
        JobCancellationRegistry cancellations,
        ITempFileStore tempFileStore,
        IHubContext<TranscriptionHub> hubContext,
        ILogger<AudioJob> logger)
    {
        var job = await dbContext.AudioJobs.FindAsync(id);
        if (job is null)
            return JobNotFound(id);

        // A running job is stopped first; the worker notices the deletion and ends quietly
        cancellations.Cancel(id);

        dbContext.AudioJobs.Remove(job);
        await dbContext.SaveChangesAsync();
        tempFileStore.DeleteUpload(tempFileStore.GetUploadPath(job.Id, job.FileName));

        // Only the id is logged: file names can contain personal data
        logger.LogInformation("Audio job {JobId} deleted", id);
        await hubContext.Clients.All.SendAsync("JobDeleted", id);

        return TypedResults.NoContent();
    }

    private static async Task<Results<Accepted, NotFound<ProblemDetails>, Conflict<ProblemDetails>>> CancelAudioJob(
        Guid id,
        AppDbContext dbContext,
        JobCancellationRegistry cancellations,
        IHubContext<TranscriptionHub> hubContext)
    {
        var job = await dbContext.AudioJobs.FindAsync(id);
        if (job is null)
            return JobNotFound(id);

        if (job.Status is not (AudioJobStatus.Pending or AudioJobStatus.Processing))
            return JobConflict($"Only pending or processing jobs can be cancelled (current status: {job.Status}).");

        // A running job is stopped by the worker, which then sets and broadcasts Cancelled
        if (job.Status == AudioJobStatus.Processing && cancellations.Cancel(id))
            return TypedResults.Accepted($"/api/audio-jobs/{id}");

        // Pending (or a stale Processing job no worker is running): the worker will skip it
        job.Status = AudioJobStatus.Cancelled;
        job.CompletedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();
        await hubContext.Clients.All.SendAsync("JobStatusChanged", ToListDto(job));

        return TypedResults.Accepted($"/api/audio-jobs/{id}");
    }

    private static async Task<Results<Accepted, NotFound<ProblemDetails>, Conflict<ProblemDetails>, ProblemHttpResult>> RetryAudioJob(
        Guid id,
        AppDbContext dbContext,
        TranscriptionQueue queue,
        ITempFileStore tempFileStore,
        IHubContext<TranscriptionHub> hubContext)
    {
        var job = await dbContext.AudioJobs.FindAsync(id);
        if (job is null)
            return JobNotFound(id);

        if (job.Status is not (AudioJobStatus.Failed or AudioJobStatus.Cancelled))
            return JobConflict($"Only failed or cancelled jobs can be retried (current status: {job.Status}).");

        var filePath = tempFileStore.GetUploadPath(job.Id, job.FileName);
        if (!File.Exists(filePath))
        {
            return TypedResults.Problem(
                title: "Upload no longer available",
                detail: "The uploaded file of this job was already removed. Please upload it again.",
                statusCode: StatusCodes.Status410Gone);
        }

        job.Status = AudioJobStatus.Pending;
        job.ErrorMessage = null;
        job.RawTranscript = null;
        job.CompletedAtUtc = null;
        // Variants (S10) were generated from the old raw transcript, which is gone once retried
        dbContext.TranscriptVariants.RemoveRange(dbContext.TranscriptVariants.Where(v => v.AudioJobId == id));
        await dbContext.SaveChangesAsync();

        await hubContext.Clients.All.SendAsync("JobStatusChanged", ToListDto(job));
        await queue.EnqueueAsync(new TranscriptionJobRequest(job.Id, filePath));

        return TypedResults.Accepted($"/api/audio-jobs/{id}");
    }

    internal static NotFound<ProblemDetails> JobNotFound(Guid id) =>
        TypedResults.NotFound(new ProblemDetails
        {
            Title = "Job not found",
            Detail = $"No audio job with ID '{id}' was found.",
            Status = StatusCodes.Status404NotFound
        });

    internal static Conflict<ProblemDetails> JobConflict(string detail) =>
        TypedResults.Conflict(new ProblemDetails
        {
            Title = "Invalid job status",
            Detail = detail,
            Status = StatusCodes.Status409Conflict
        });

    private static AudioJobListDto ToListDto(AudioJob job) =>
        new(job.Id, job.FileName, job.FileSizeBytes,
            job.Status, job.Language, job.DurationSeconds,
            job.CreatedAtUtc, job.CompletedAtUtc);

    private static async Task<Results<Accepted<CreateAudioJobResponse>, ValidationProblem, ProblemHttpResult>> UploadAudioJob(
        HttpRequest request,
        AppDbContext dbContext,
        TranscriptionQueue queue,
        IOptions<UploadOptions> uploadOptions,
        IOptions<WhisperOptions> whisperOptions,
        IOptions<DiarizationOptions> diarizationOptions,
        IHubContext<TranscriptionHub> hubContext,
        ITempFileStore tempFileStore,
        ILogger<AudioJob> logger,
        CancellationToken cancellationToken)
    {
        var options = uploadOptions.Value;

        if (!request.HasFormContentType)
        {
            return TypedResults.Problem(
                title: "Invalid request",
                detail: "Expected a multipart/form-data request.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Streamed straight to disk (S14): a 400 MB upload must not be buffered whole in memory, unlike
        // ASP.NET Core's IFormFile model binding (see LargeUploadMemoryTests for the measured difference)
        var jobId = Guid.NewGuid();
        var upload = await MultipartUploadParser.ParseAsync(request, jobId, tempFileStore, options.MaxFileSizeBytes, cancellationToken);
        if (upload is null)
        {
            return TypedResults.Problem(
                title: "Invalid request",
                detail: "Missing multipart boundary.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (upload.FileTooLarge)
        {
            return TypedResults.Problem(
                title: "File too large",
                detail: $"File size exceeds the maximum allowed size ({options.MaxFileSizeBytes} bytes).",
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        // Model and language must come from the allowlists (see GET /api/transcription-options)
        var settingErrors = new Dictionary<string, string[]>();
        if (!whisperOptions.Value.TryResolveModel(upload.Model, out var resolvedModel))
            settingErrors["model"] = [$"Model '{upload.Model}' is not available. Allowed models: {string.Join(", ", whisperOptions.Value.AllowedModels)}."];
        if (!whisperOptions.Value.TryResolveLanguage(upload.Language, out var resolvedLanguage))
            settingErrors["language"] = [$"Language '{upload.Language}' is not supported. Use '{WhisperOptions.AutomaticLanguage}' or one of: {string.Join(", ", whisperOptions.Value.SupportedLanguages)}."];
        if (upload.Diarize && !diarizationOptions.Value.Enabled)
            settingErrors["diarize"] = ["Speaker diarization is not configured on this server."];
        if (settingErrors.Count > 0)
        {
            if (upload.FilePath is not null)
                File.Delete(upload.FilePath);
            return TypedResults.ValidationProblem(settingErrors);
        }

        // Validate file presence
        if (!upload.HasFile || upload.FileSizeBytes == 0)
        {
            return TypedResults.Problem(
                title: "No file provided",
                detail: "A valid audio file must be uploaded.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Validate content type; compared without parameters, e.g. browsers send "audio/webm;codecs=opus" (S12)
        var contentType = (upload.FileContentType ?? "").ToLowerInvariant().Split(';')[0].Trim();
        if (!options.AllowedContentTypes.Contains(contentType))
        {
            File.Delete(upload.FilePath!);
            return TypedResults.Problem(
                title: "Invalid file type",
                detail: $"Content type '{contentType}' is not allowed. Allowed types: {string.Join(", ", options.AllowedContentTypes)}.",
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        // Validate magic bytes
        using var headerStream = new MemoryStream(upload.HeaderBytes.ToArray());
        if (!MagicBytesValidator.IsValid(contentType, headerStream))
        {
            File.Delete(upload.FilePath!);
            return TypedResults.Problem(
                title: "Invalid file content",
                detail: "The file content does not match the declared content type. The file may be corrupted or mislabeled.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        // Create database record
        var audioJob = new AudioJob
        {
            Id = jobId,
            FileName = upload.FileName!,
            FileSizeBytes = upload.FileSizeBytes,
            ContentType = contentType,
            Status = AudioJobStatus.Pending,
            Model = resolvedModel,
            RequestedLanguage = resolvedLanguage,
            DiarizationRequested = upload.Diarize,
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.AudioJobs.Add(audioJob);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Audio job {JobId} created for file '{FileName}' ({FileSize} bytes)",
            jobId, upload.FileName, upload.FileSizeBytes);

        // Notify connected clients about new job
        await hubContext.Clients.All.SendAsync("JobCreated", new AudioJobListDto(
            audioJob.Id, audioJob.FileName, audioJob.FileSizeBytes,
            audioJob.Status, audioJob.Language, audioJob.DurationSeconds,
            audioJob.CreatedAtUtc, audioJob.CompletedAtUtc));

        // Enqueue the job for background processing
        await queue.EnqueueAsync(new TranscriptionJobRequest(jobId, upload.FilePath!));

        return TypedResults.Accepted($"/api/audio-jobs/{jobId}", new CreateAudioJobResponse(jobId));
    }

    private static async Task<Ok<PaginatedResult<AudioJobListDto>>> GetAudioJobs(
        AppDbContext dbContext,
        JobProgressStore progressStore,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var totalCount = await dbContext.AudioJobs.CountAsync();

        var items = await dbContext.AudioJobs
            .OrderByDescending(j => j.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(j => new AudioJobListDto(
                j.Id, j.FileName, j.FileSizeBytes,
                j.Status, j.Language, j.DurationSeconds,
                j.CreatedAtUtc, j.CompletedAtUtc))
            .ToListAsync();

        // Progress lives in memory, not in the database
        items = items.Select(j => j.Status == AudioJobStatus.Processing ? j with { ProgressPercent = progressStore.Get(j.Id) } : j).ToList();

        return TypedResults.Ok(new PaginatedResult<AudioJobListDto>(items, totalCount, page, pageSize));
    }

    private static async Task<Results<Ok<AudioJobDto>, NotFound<ProblemDetails>>> GetAudioJob(
        Guid id,
        AppDbContext dbContext,
        JobProgressStore progressStore)
    {
        var job = await dbContext.AudioJobs.FindAsync(id);

        if (job is null)
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "Job not found",
                Detail = $"No audio job with ID '{id}' was found.",
                Status = StatusCodes.Status404NotFound
            });
        }

        return TypedResults.Ok(new AudioJobDto(
            job.Id, job.FileName, job.FileSizeBytes, job.ContentType,
            job.Status, job.RawTranscript, job.ErrorMessage,
            job.Language, job.DurationSeconds,
            job.CreatedAtUtc, job.CompletedAtUtc,
            job.Model, job.RequestedLanguage, job.DiarizationRequested,
            job.Status == AudioJobStatus.Processing ? progressStore.Get(job.Id) : null));
    }
}
