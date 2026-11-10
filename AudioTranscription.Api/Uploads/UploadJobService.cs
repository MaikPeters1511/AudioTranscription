using AudioTranscription.Api.Access;
using AudioTranscription.Api.BackgroundServices;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Glossary;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using AudioTranscription.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudioTranscription.Api.Uploads;

/// <summary>The transcription settings a client sends with an upload, as strings (not yet checked).</summary>
public record RequestedUploadSettings(string? Model, string? Language, bool Diarize, string? GlossaryId);

/// <summary>Settings after validation against the allowlists; the glossary is already turned into the initial prompt.</summary>
public record ResolvedUploadSettings(string Model, string? Language, bool Diarize, string? InitialPrompt);

/// <summary>
/// What both upload paths (multipart, S14, and resumable tus, S27) share once the file is on disk: checking
/// the requested settings and creating, announcing and queueing the job.
/// </summary>
public class UploadJobService(
    AppDbContext dbContext,
    TranscriptionQueue queue,
    IJobNotifier notifier,
    IOptions<WhisperOptions> whisperOptions,
    IOptions<DiarizationOptions> diarizationOptions,
    ILogger<UploadJobService> logger)
{
    /// <summary>Model and language must come from the allowlists, a glossary must be the user's own.</summary>
    public async Task<(ResolvedUploadSettings? Settings, Dictionary<string, string[]> Errors)> ResolveSettingsAsync(
        RequestedUploadSettings requested, string ownerId, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var whisper = whisperOptions.Value;
        if (!whisper.TryResolveModel(requested.Model, out var model))
            errors["model"] = [$"Model '{requested.Model}' is not available. Allowed models: {string.Join(", ", whisper.AllowedModels)}."];
        if (!whisper.TryResolveLanguage(requested.Language, out var language))
            errors["language"] = [$"Language '{requested.Language}' is not supported. Use '{WhisperOptions.AutomaticLanguage}' or one of: {string.Join(", ", whisper.SupportedLanguages)}."];
        if (requested.Diarize && !diarizationOptions.Value.Enabled)
            errors["diarize"] = ["Speaker diarization is not configured on this server."];

        // A glossary (S22) must be the user's own; someone else's id is as good as a missing one
        string? initialPrompt = null;
        if (!string.IsNullOrWhiteSpace(requested.GlossaryId))
        {
            if (!Guid.TryParse(requested.GlossaryId, out var glossaryId))
            {
                errors["glossaryId"] = ["Must be a valid glossary id."];
            }
            else
            {
                var terms = await dbContext.Glossaries
                    .Where(g => g.Id == glossaryId && g.OwnerId == ownerId)
                    .Select(g => g.Terms.OrderBy(t => t.Position).Select(t => t.Term).ToList())
                    .FirstOrDefaultAsync(cancellationToken);
                if (terms is null)
                    errors["glossaryId"] = ["Unknown glossary."];
                else if (terms.Count > 0)
                    initialPrompt = GlossaryPrompt.Build(terms);
            }
        }

        return errors.Count > 0
            ? (null, errors)
            : (new ResolvedUploadSettings(model, language, requested.Diarize, initialPrompt), errors);
    }

    /// <summary>Saves the job, tells the owner's clients and hands the file to the transcription queue.</summary>
    public async Task<AudioJob> CreateAndEnqueueAsync(
        Guid jobId, string ownerId, string fileName, long fileSizeBytes, string contentType, string filePath, ResolvedUploadSettings settings)
    {
        var audioJob = new AudioJob
        {
            Id = jobId,
            FileName = fileName,
            FileSizeBytes = fileSizeBytes,
            ContentType = contentType,
            Status = AudioJobStatus.Pending,
            Model = settings.Model,
            RequestedLanguage = settings.Language,
            DiarizationRequested = settings.Diarize,
            InitialPrompt = settings.InitialPrompt,
            OwnerId = ownerId,
            CreatedAtUtc = DateTime.UtcNow,
            QueuedAtUtc = DateTime.UtcNow
        };

        dbContext.AudioJobs.Add(audioJob);
        await dbContext.SaveChangesAsync();

        // Only the id is logged: file names can contain personal data
        logger.LogInformation("Audio job {JobId} created ({FileSize} bytes)", jobId, fileSizeBytes);

        await notifier.NotifyAsync(audioJob.Id, "JobCreated", new AudioJobListDto(
            audioJob.Id, audioJob.FileName, audioJob.FileSizeBytes,
            audioJob.Status, audioJob.Language, audioJob.DurationSeconds,
            audioJob.CreatedAtUtc, audioJob.CompletedAtUtc));

        await queue.EnqueueAsync(new TranscriptionJobRequest(jobId, filePath));
        return audioJob;
    }
}
