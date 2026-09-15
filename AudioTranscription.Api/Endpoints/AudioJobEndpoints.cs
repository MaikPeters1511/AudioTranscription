using AudioTranscription.Api.BackgroundServices;
using AudioTranscription.Api.Configuration;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Api.Hubs;
using AudioTranscription.Api.Validation;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
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
    }

    private static async Task<Results<Accepted<CreateAudioJobResponse>, ValidationProblem, ProblemHttpResult>> UploadAudioJob(
        IFormFile file,
        AppDbContext dbContext,
        TranscriptionQueue queue,
        IOptions<UploadOptions> uploadOptions,
        IHubContext<TranscriptionHub> hubContext,
        ILogger<AudioJob> logger)
    {
        var options = uploadOptions.Value;

        // Validate file presence
        if (file is null || file.Length == 0)
        {
            return TypedResults.Problem(
                title: "No file provided",
                detail: "A valid audio file must be uploaded.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Validate file size
        if (file.Length > options.MaxFileSizeBytes)
        {
            return TypedResults.Problem(
                title: "File too large",
                detail: $"File size ({file.Length} bytes) exceeds the maximum allowed size ({options.MaxFileSizeBytes} bytes).",
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        // Validate content type
        var contentType = file.ContentType.ToLowerInvariant();
        if (!options.AllowedContentTypes.Contains(contentType))
        {
            return TypedResults.Problem(
                title: "Invalid file type",
                detail: $"Content type '{contentType}' is not allowed. Allowed types: {string.Join(", ", options.AllowedContentTypes)}.",
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        // Validate magic bytes
        using var stream = file.OpenReadStream();
        if (!MagicBytesValidator.IsValid(contentType, stream))
        {
            return TypedResults.Problem(
                title: "Invalid file content",
                detail: "The file content does not match the declared content type. The file may be corrupted or mislabeled.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        // Save file to temp storage
        var tempPath = Path.Combine(Directory.GetCurrentDirectory(), options.TempStoragePath);
        Directory.CreateDirectory(tempPath);

        var jobId = Guid.NewGuid();
        var fileExtension = Path.GetExtension(file.FileName);
        var tempFileName = $"{jobId}{fileExtension}";
        var filePath = Path.Combine(tempPath, tempFileName);

        stream.Position = 0;
        await using (var fileStream = new FileStream(filePath, FileMode.Create))
        {
            await stream.CopyToAsync(fileStream);
        }

        // Create database record
        var audioJob = new AudioJob
        {
            Id = jobId,
            FileName = file.FileName,
            FileSizeBytes = file.Length,
            ContentType = contentType,
            Status = AudioJobStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.AudioJobs.Add(audioJob);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Audio job {JobId} created for file '{FileName}' ({FileSize} bytes)",
            jobId, file.FileName, file.Length);

        // Notify connected clients about new job
        await hubContext.Clients.All.SendAsync("JobCreated", new AudioJobListDto(
            audioJob.Id, audioJob.FileName, audioJob.FileSizeBytes,
            audioJob.Status, audioJob.Language, audioJob.DurationSeconds,
            audioJob.CreatedAtUtc, audioJob.CompletedAtUtc));

        // Enqueue the job for background processing
        await queue.EnqueueAsync(new TranscriptionJobRequest(jobId, filePath));

        return TypedResults.Accepted($"/api/audio-jobs/{jobId}", new CreateAudioJobResponse(jobId));
    }

    private static async Task<Ok<PaginatedResult<AudioJobListDto>>> GetAudioJobs(
        AppDbContext dbContext,
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

        return TypedResults.Ok(new PaginatedResult<AudioJobListDto>(items, totalCount, page, pageSize));
    }

    private static async Task<Results<Ok<AudioJobDto>, NotFound<ProblemDetails>>> GetAudioJob(
        Guid id,
        AppDbContext dbContext)
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
            job.Status, job.TranscriptText, job.ErrorMessage,
            job.Language, job.DurationSeconds,
            job.CreatedAtUtc, job.CompletedAtUtc));
    }
}
