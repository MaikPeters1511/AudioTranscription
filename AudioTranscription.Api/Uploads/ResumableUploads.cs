using System.Net;
using System.Text;
using AudioTranscription.Api.Access;
using AudioTranscription.Api.BackgroundServices;
using AudioTranscription.Api.Configuration;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Api.Storage;
using AudioTranscription.Api.Validation;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using tusdotnet;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;
using tusdotnet.Stores;

namespace AudioTranscription.Api.Uploads;

/// <summary>
/// Resumable uploads over the tus protocol (S27, ADR 0006): the client creates an upload (POST), asks how far
/// it got (HEAD) and appends chunks (PATCH). Partial files live in the <c>tus</c> folder of the upload storage;
/// the finished file is validated, moved next to the other uploads and turned into a job exactly like a
/// multipart upload. Every session belongs to the user who created it.
/// </summary>
public static class ResumableUploads
{
    public const string UrlPath = "/api/uploads";
    private const int HeaderByteCount = 16; // enough for every magic-bytes check MagicBytesValidator does

    public static void AddResumableUploads(this IServiceCollection services)
    {
        services.AddSingleton<ResumableUploadStore>();
        services.AddScoped<UploadJobService>();
        services.AddSingleton<IncompleteUploadCleanupService>();
        services.AddHostedService(sp => sp.GetRequiredService<IncompleteUploadCleanupService>());
    }

    public static void UseResumableUploads(this WebApplication app)
    {
        // tusdotnet answers with an absolute Location built from the request's host; behind a reverse proxy that
        // host and scheme are the internal ones. A relative Location is valid and the client resolves it itself.
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(UrlPath))
            {
                context.Response.OnStarting(() =>
                {
                    var location = context.Response.Headers.Location.ToString();
                    if (Uri.TryCreate(location, UriKind.Absolute, out var absolute))
                        context.Response.Headers.Location = absolute.PathAndQuery;
                    return Task.CompletedTask;
                });
            }
            await next();
        });

        app.UseTus(httpContext =>
        {
            var options = httpContext.RequestServices.GetRequiredService<IOptions<UploadOptions>>().Value;
            return Task.FromResult(new DefaultTusConfiguration
            {
                UrlPath = UrlPath,
                Store = httpContext.RequestServices.GetRequiredService<ResumableUploadStore>().Store,
                MaxAllowedUploadSizeInBytesLong = options.MaxFileSizeBytes,
                Events = new Events
                {
                    OnAuthorizeAsync = OnAuthorizeAsync,
                    OnBeforeCreateAsync = OnBeforeCreateAsync,
                    OnCreateCompleteAsync = OnCreateCompleteAsync,
                    OnFileCompleteAsync = OnFileCompleteAsync,
                },
            });
        });
    }

    public static void MapResumableUploadEndpoints(this WebApplication app)
    {
        app.MapGet($"{UrlPath}/{{uploadId}}/job", GetJob)
            .WithName("GetResumableUploadJob")
            .WithTags("Resumable upload")
            .WithSummary("Job of a finished resumable upload")
            .WithDescription("200 with the job id once the last chunk was processed, 409 while the upload is incomplete, the rejection status (e.g. 422) if the assembled file was refused");
    }

    // --- tus events ---------------------------------------------------------------

    private static async Task OnAuthorizeAsync(AuthorizeContext context)
    {
        var http = context.HttpContext;
        if (context.Intent == IntentType.GetOptions)
            return;

        var userId = JobAccessService.GetUserId(http.User);
        if (string.IsNullOrEmpty(userId))
        {
            context.FailRequest(HttpStatusCode.Unauthorized, "Not signed in.");
            return;
        }

        if (context.Intent == IntentType.CreateFile)
            return;

        // Everything else addresses an existing upload: it has to be the user's own, anything else does not exist
        var session = await FindSessionAsync(http, UploadIdOf(http.Request.Path));
        if (session is null || session.OwnerId != userId)
        {
            context.FailRequest(HttpStatusCode.NotFound, "Upload not found.");
            return;
        }

        // The partial file is gone once an upload is finished; a late or repeated chunk is a conflict, not a 404
        if (context.Intent == IntentType.WriteFile && session.IsClosed)
            context.FailRequest(HttpStatusCode.Conflict, "This upload is already complete.");
    }

    private static async Task OnBeforeCreateAsync(BeforeCreateContext context)
    {
        var options = context.HttpContext.RequestServices.GetRequiredService<IOptions<UploadOptions>>().Value;

        var length = context.UploadLength;
        if (length <= 0)
        {
            context.FailRequest(HttpStatusCode.BadRequest, "Upload-Length is required and must be greater than 0.");
            return;
        }
        if (length > options.MaxFileSizeBytes)
        {
            context.FailRequest(HttpStatusCode.RequestEntityTooLarge, $"File size exceeds the maximum allowed size ({options.MaxFileSizeBytes} bytes).");
            return;
        }

        var fileName = MetadataValue(context.Metadata, "filename");
        if (string.IsNullOrWhiteSpace(fileName))
        {
            context.FailRequest(HttpStatusCode.BadRequest, "The metadata 'filename' is required.");
            return;
        }

        var contentType = NormalizeContentType(MetadataValue(context.Metadata, "filetype"));
        if (!options.AllowedContentTypes.Contains(contentType))
        {
            context.FailRequest(HttpStatusCode.UnsupportedMediaType, $"Content type '{contentType}' is not allowed. Allowed types: {string.Join(", ", options.AllowedContentTypes)}.");
            return;
        }

        // Refuse unusable settings now instead of letting the user wait for the last chunk
        var uploads = context.HttpContext.RequestServices.GetRequiredService<UploadJobService>();
        var ownerId = JobAccessService.GetUserId(context.HttpContext.User)!;
        var (settings, errors) = await uploads.ResolveSettingsAsync(SettingsOf(context.Metadata), ownerId, context.HttpContext.RequestAborted);
        if (settings is null)
            context.FailRequest(HttpStatusCode.BadRequest, string.Join(" ", errors.SelectMany(e => e.Value)));
    }

    private static async Task OnCreateCompleteAsync(CreateCompleteContext context)
    {
        var http = context.HttpContext;
        var db = http.RequestServices.GetRequiredService<AppDbContext>();
        db.UploadSessions.Add(new UploadSession
        {
            Id = context.FileId,
            OwnerId = JobAccessService.GetUserId(http.User)!,
            CreatedAtUtc = http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync(http.RequestAborted);
    }

    private static async Task OnFileCompleteAsync(FileCompleteContext context)
    {
        var http = context.HttpContext;
        var services = http.RequestServices;
        var db = services.GetRequiredService<AppDbContext>();
        var uploadStore = services.GetRequiredService<ResumableUploadStore>();
        var cancellationToken = http.RequestAborted;

        var session = await db.UploadSessions.SingleAsync(s => s.Id == context.FileId, cancellationToken);
        if (session.IsClosed)
            return; // each upload creates its job at most once

        var file = await context.GetFileAsync();
        var metadata = await file.GetMetadataAsync(cancellationToken);
        var fileName = MetadataValue(metadata, "filename") ?? "upload";
        var contentType = NormalizeContentType(MetadataValue(metadata, "filetype"));
        var sourcePath = uploadStore.PathOf(context.FileId);

        async Task RejectAsync(int status, string detail)
        {
            await uploadStore.Store.DeleteFileAsync(context.FileId, cancellationToken);
            session.RejectionStatus = status;
            session.RejectionDetail = detail;
            await db.SaveChangesAsync(cancellationToken);
        }

        // The checks run on the assembled file, whatever the client declared when it started
        var size = new FileInfo(sourcePath).Length;
        var header = new byte[HeaderByteCount];
        int read;
        await using (var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            read = await stream.ReadAsync(header, cancellationToken);
        using var headerStream = new MemoryStream(header, 0, read);
        if (!MagicBytesValidator.IsValid(contentType, headerStream))
        {
            await RejectAsync(StatusCodes.Status422UnprocessableEntity,
                "The file content does not match the declared content type. The file may be corrupted or mislabeled.");
            return;
        }

        var uploads = services.GetRequiredService<UploadJobService>();
        var (settings, errors) = await uploads.ResolveSettingsAsync(SettingsOf(metadata), session.OwnerId, cancellationToken);
        if (settings is null)
        {
            await RejectAsync(StatusCodes.Status400BadRequest, string.Join(" ", errors.SelectMany(e => e.Value)));
            return;
        }

        var tempFileStore = services.GetRequiredService<ITempFileStore>();
        var jobId = Guid.NewGuid();
        var targetPath = tempFileStore.GetUploadPath(jobId, fileName);
        Directory.CreateDirectory(tempFileStore.StorageDirectory);
        File.Move(sourcePath, targetPath);
        await uploadStore.Store.DeleteFileAsync(context.FileId, cancellationToken); // leftover tus bookkeeping files

        await uploads.CreateAndEnqueueAsync(jobId, session.OwnerId, fileName, size, contentType, targetPath, settings);
        session.JobId = jobId;
        await db.SaveChangesAsync(cancellationToken);
    }

    // --- job lookup ------------------------------------------------------------------

    private static async Task<Results<Ok<CreateAudioJobResponse>, NotFound, Conflict<ProblemDetails>, ProblemHttpResult>> GetJob(
        string uploadId, HttpContext http, AppDbContext db)
    {
        var userId = JobAccessService.GetUserId(http.User);
        var session = await db.UploadSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == uploadId);
        if (session is null || session.OwnerId != userId)
            return TypedResults.NotFound();

        if (session.JobId is { } jobId)
            return TypedResults.Ok(new CreateAudioJobResponse(jobId));

        if (session.RejectionStatus is { } status)
        {
            return TypedResults.Problem(
                title: "Upload rejected",
                detail: session.RejectionDetail,
                statusCode: status);
        }

        return TypedResults.Conflict(new ProblemDetails
        {
            Title = "Upload incomplete",
            Detail = "Not all bytes of this upload have arrived yet.",
            Status = StatusCodes.Status409Conflict,
        });
    }

    // --- helpers ---------------------------------------------------------------------

    private static string UploadIdOf(PathString path) =>
        path.Value?.TrimEnd('/').Split('/').LastOrDefault() ?? string.Empty;

    private static Task<UploadSession?> FindSessionAsync(HttpContext http, string id) =>
        http.RequestServices.GetRequiredService<AppDbContext>().UploadSessions
            .AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, http.RequestAborted);

    private static string? MetadataValue(Dictionary<string, Metadata> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && !value.HasEmptyValue ? value.GetString(Encoding.UTF8) : null;

    private static string NormalizeContentType(string? contentType) =>
        (contentType ?? string.Empty).ToLowerInvariant().Split(';')[0].Trim();

    private static RequestedUploadSettings SettingsOf(Dictionary<string, Metadata> metadata) => new(
        MetadataValue(metadata, "model"),
        MetadataValue(metadata, "language"),
        string.Equals(MetadataValue(metadata, "diarize"), "true", StringComparison.OrdinalIgnoreCase),
        MetadataValue(metadata, "glossaryId"));
}

/// <summary>The tus disk store of the partial uploads: the <c>tus</c> folder inside the upload storage.</summary>
public class ResumableUploadStore
{
    public ResumableUploadStore(ITempFileStore tempFileStore)
    {
        Directory = Path.Combine(tempFileStore.StorageDirectory, "tus");
        System.IO.Directory.CreateDirectory(Directory);
        Store = new TusDiskStore(Directory);
    }

    public string Directory { get; }
    public TusDiskStore Store { get; }

    /// <summary>Where tus keeps the bytes of an upload (the id is the file name).</summary>
    public string PathOf(string fileId) => Path.Combine(Directory, fileId);
}
