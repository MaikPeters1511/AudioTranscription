using AudioTranscription.Api.Chat;
using AudioTranscription.Api.Search;
using AudioTranscription.Api.Uploads;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using AudioTranscription.Api.Health;
using AudioTranscription.Api.Telemetry;
using AudioTranscription.Api.Access;
using AudioTranscription.Api.Automation;
using AudioTranscription.Api.Auth;
using AudioTranscription.Api.BackgroundServices;
using AudioTranscription.Api.Configuration;
using AudioTranscription.Api.Endpoints;
using AudioTranscription.Api.Hubs;
using AudioTranscription.Api.Storage;
using AudioTranscription.Domain.Export;
using AudioTranscription.Infrastructure.Export;
using AudioTranscription.Infrastructure.Data;
using AudioTranscription.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire ServiceDefaults (OpenTelemetry, Health Checks, etc.)
builder.AddServiceDefaults();

// Add EF Core with Aspire-managed SQL Server connection
builder.AddSqlServerDbContext<AppDbContext>("transcriptiondb");

// Bind configuration
builder.Services.Configure<UploadOptions>(
    builder.Configuration.GetSection(UploadOptions.SectionName));

// Register transcription services
builder.Services.AddSingleton<TranscriptionQueue>();
builder.Services.AddSingleton<TranscriptionMetrics>(); // S29: business metrics (meter "AudioTranscription")
builder.Services.AddSingleton<JobCancellationRegistry>();
builder.Services.AddSingleton<JobProgressStore>();
builder.Services.AddSingleton<ITempFileStore, TempFileStore>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Audio16kHzWavConverter>();
// Singleton: loaded Whisper models are kept and shared between jobs
builder.Services.AddOptions<WhisperOptions>()
    .Bind(builder.Configuration.GetSection(WhisperOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<WhisperOptions>, WhisperOptionsValidator>();
builder.Services.AddSingleton<ITranscriptionService, WhisperTranscriptionService>();
// Must start before JobRecoveryService/TranscriptionWorker: applies Whisper:RuntimeOrder (S15)
// before the first model load can happen.
builder.Services.AddHostedService<WhisperRuntimeConfigurator>();

// Speaker diarization (S11, ADR 0004): optional, needs local ONNX models the operator supplies
builder.Services.AddOptions<DiarizationOptions>()
    .Bind(builder.Configuration.GetSection(DiarizationOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<DiarizationOptions>, DiarizationOptionsValidator>();
if (builder.Configuration.GetValue<bool>($"{DiarizationOptions.SectionName}:Enabled"))
{
    builder.Services.AddSingleton<IDiarizationService, SherpaOnnxDiarizationService>();
}
builder.Services.AddHostedService<InitialUserSeeder>();
builder.Services.AddOptions<RetentionOptions>()
    .Bind(builder.Configuration.GetSection(RetentionOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<RetentionOptions>, RetentionOptionsValidator>();
builder.Services.AddHostedService<OrphanedUploadCleanupService>();
builder.Services.AddResumableUploads(); // S27, ADR 0006
builder.Services.AddHostedService<RetentionService>();
builder.Services.AddHostedService<JobRecoveryService>(); // must start before the worker
builder.Services.AddHostedService<TranscriptionWorker>();

// Optional: Register the LLM-backed variant generator (S10; formerly ITranscriptPostProcessor/S04)
// if an Ollama connection string is present.
var ollamaConnectionString = builder.Configuration.GetConnectionString("llama3.2");
if (!string.IsNullOrWhiteSpace(ollamaConnectionString))
{
    builder.Services.AddSingleton<Microsoft.Extensions.AI.IChatClient>(new Microsoft.Extensions.AI.OllamaChatClient(new Uri(ollamaConnectionString), "llama3.2"));
    builder.Services.Configure<PostProcessingOptions>(builder.Configuration.GetSection(PostProcessingOptions.SectionName));
    builder.Services.AddSingleton<IPostProcessingPromptCatalog, PostProcessingPromptCatalog>();
    builder.Services.AddScoped<IVariantGenerator, OllamaVariantGenerator>();
    builder.Services.AddSingleton<VariantQueue>();
    builder.Services.AddHostedService<VariantRecoveryService>(); // must start before the worker
    builder.Services.AddScoped<ITemplateVariantGenerator, LlmTemplateVariantGenerator>(); // S26
    builder.Services.AddHostedService<VariantWorker>();
    builder.Services.AddScoped<IChapterGenerator, LlmChapterGenerator>(); // S25
    builder.Services.AddSingleton<ChapterQueue>();
    builder.Services.AddHostedService<ChapterWorker>();
}

// Automation (S28, ADR 0009): API keys, webhooks, watched folders
// Webhook secrets are encrypted with Data Protection. Without persisted keys they (and cookie sessions) do not survive
// a restart of a container, so the operator can point DataProtection:KeysPath to a persistent folder.
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } dataProtectionKeysPath)
    builder.Services.AddDataProtection().SetApplicationName("AudioTranscription")
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
builder.Services.Configure<ApiKeyOptions>(builder.Configuration.GetSection(ApiKeyOptions.SectionName));
builder.Services.Configure<WebhookOptions>(builder.Configuration.GetSection(WebhookOptions.SectionName));
builder.Services.AddSingleton<WebhookSecretProtector>();
builder.Services.AddScoped<WebhookSender>();
builder.Services.AddScoped<IWebhookDispatcher, WebhookDispatcher>();
builder.Services.AddScoped<WebhookDeliveryProcessor>();
builder.Services.AddHostedService<WebhookDeliveryService>();
// Own client (no shared resilience/service-discovery defaults): no redirects, target address checked when connecting
builder.Services.AddSingleton(sp => WebhookHttpClient.Create(sp.GetRequiredService<IOptions<WebhookOptions>>().Value));
builder.Services.AddOptions<WatchOptions>()
    .Bind(builder.Configuration.GetSection(WatchOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<WatchOptions>, WatchOptionsValidator>();
builder.Services.AddScoped<FolderWatchProcessor>();
var watchSettings = builder.Configuration.GetSection(WatchOptions.SectionName).Get<WatchOptions>();
if (watchSettings is { Enabled: true, Folders.Count: > 0 })
    builder.Services.AddHostedService<FolderWatchService>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Requests with an API key: per authenticated key. Requests whose key was refused count per remote address, so
    // someone who knows only a key's public prefix cannot use up that key's budget.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        if (!ApiKeyAuthentication.IsApiKeyRequest(context.Request))
            return RateLimitPartition.GetNoLimiter("session");
        var partition = context.User.FindFirst(ApiKeyAuthentication.KeyIdClaim)?.Value is { } keyId
            ? "key:" + keyId
            : "ip:" + context.Connection.RemoteIpAddress;
        var perMinute = Math.Max(1, context.RequestServices.GetRequiredService<IOptions<ApiKeyOptions>>().Value.RequestsPerMinute);
        return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
        });
    });
    options.AddPolicy(WebhookEndpoints.TestRateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            JobAccessService.GetUserId(context.User) ?? "ip:" + context.Connection.RemoteIpAddress,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// Add SignalR
builder.Services.AddSignalR();
builder.Services.AddSingleton<ITranscriptExporter, MarkdownExporter>(); // S21
builder.Services.AddSingleton<ITranscriptExporter, JsonExporter>();
builder.Services.AddSingleton<ITranscriptExporter, DocxExporter>();
builder.Services.AddSingleton<ITranscriptExporter, PdfExporter>();
builder.Services.AddScoped<JobAccessService>();
builder.Services.AddScoped<IJobNotifier, JobNotifier>();

// OpenAPI (EN-3): describes every mapped endpoint, served under the same auth policy as the rest
// of the API (the fallback policy below), not anonymously.
builder.Services.AddOpenApi(options => options.AddDocumentTransformer<OpenApiSecuritySchemes>());

// Readiness checks (S29-T4) for /health (served in Development only, see ServiceDefaults)
var healthChecks = builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
    .AddCheck("ffmpeg", new FfmpegHealthCheck(), tags: ["ready"])
    .Add(new HealthCheckRegistration("whisper-model", sp =>
    {
        var whisper = sp.GetRequiredService<IOptions<WhisperOptions>>().Value;
        return new WhisperModelHealthCheck(Path.Combine(AppContext.BaseDirectory, whisper.ModelsDirectory), whisper.DefaultModel);
    }, failureStatus: null, tags: ["ready"]));
if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("llama3.2")))
{
    var ollamaAddress = new Uri(builder.Configuration.GetConnectionString("llama3.2")!);
    healthChecks.AddCheck("ollama", new OllamaHealthCheck(new HttpClient { Timeout = TimeSpan.FromSeconds(3) }, ollamaAddress), tags: ["ready"]);
}

// Semantic search (S23, ADR 0010): only with a Qdrant connection string and a usable embedding model
var semanticSearchAvailable = builder.AddSemanticSearch();
builder.AddChat(semanticSearchAvailable); // S24, ADR 0011

// Authentication: local ASP.NET Core Identity with cookie sessions (ADR 0003)
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.AddIdentityApiEndpoints<IdentityUser>(options => options.User.RequireUniqueEmail = true)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    // API: answer with status codes instead of redirecting to a login page
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

// API keys (S28, ADR 0009): a request that carries a key is authenticated by the key scheme, every other request
// as before (cookie session or Identity bearer token).
const string AppAuthScheme = "AppAuth";
// Default scheme of AddIdentityApiEndpoints (its constant is internal): Identity bearer token or application cookie
const string IdentityBearerAndApplicationScheme = "Identity.BearerAndApplication";
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = AppAuthScheme;
        options.DefaultAuthenticateScheme = AppAuthScheme;
        options.DefaultChallengeScheme = AppAuthScheme;
    })
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthentication.Scheme, _ => { })
    .AddPolicyScheme(AppAuthScheme, AppAuthScheme, options => options.ForwardDefaultSelector = context =>
        ApiKeyAuthentication.IsApiKeyRequest(context.Request)
            ? ApiKeyAuthentication.Scheme
            : IdentityBearerAndApplicationScheme);

// Every endpoint requires a signed-in user unless it explicitly allows anonymous access
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(Roles.AdminPolicy, policy => policy.RequireRole(Roles.Admin));

// Re-check the security stamp on every request so deactivating a user or changing a password
// ends their existing sessions immediately instead of after the default 30 minutes.
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);

// CORS only for explicitly configured origins; the frontend normally is same-origin via proxy/nginx
builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>().Configure<IConfiguration>((options, configuration) =>
{
    var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials());
});

// Upload:MaxFileSizeBytes (UploadOptions) is the single source for the request size limit (S14): Kestrel
// and the multipart form parser are both derived from it here, with a fixed margin for the multipart
// boundaries and the other form fields (model/language/diarize).
const long UploadRequestOverheadBytes = 1_048_576; // 1 MiB
var maxUploadFileSizeBytes = builder.Configuration.GetValue(
    $"{UploadOptions.SectionName}:{nameof(UploadOptions.MaxFileSizeBytes)}", 500_000_000L);
var maxUploadRequestBodySize = maxUploadFileSizeBytes + UploadRequestOverheadBytes;

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maxUploadRequestBodySize;
});
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxUploadRequestBodySize;
});

var app = builder.Build();

// Ensure database is created via master connection (to avoid SQL Server Error 18456 State 38) and apply migrations
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    if (dbContext.Database.IsRelational())
    {
        var connectionString = dbContext.Database.GetConnectionString();
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            var connBuilder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
            var targetDbName = connBuilder.InitialCatalog;

            if (!string.IsNullOrEmpty(targetDbName) && !targetDbName.Equals("master", StringComparison.OrdinalIgnoreCase))
            {
                connBuilder.InitialCatalog = "master";
                var maxRetries = 10;
                for (var retry = 1; retry <= maxRetries; retry++)
                {
                    try
                    {
                        await using var masterConn = new Microsoft.Data.SqlClient.SqlConnection(connBuilder.ConnectionString);
                        await masterConn.OpenAsync();
                        await using var cmd = masterConn.CreateCommand();
                        cmd.CommandText = $"""
                            IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'{targetDbName.Replace("'", "''")}')
                            BEGIN
                                CREATE DATABASE [{targetDbName.Replace("]", "]]")}];
                            END
                            """;
                        await cmd.ExecuteNonQueryAsync();
                        break;
                    }
                    catch (Microsoft.Data.SqlClient.SqlException ex) when (retry < maxRetries)
                    {
                        logger.LogWarning(ex, "Waiting for SQL Server to become available (Attempt {Attempt}/{MaxRetries})...", retry, maxRetries);
                        await Task.Delay(2000);
                    }
                }
            }
        }
    }

    await dbContext.Database.MigrateWithBaselineAsync(logger);
}

app.UseCors();
app.UseAuthentication();
app.UseMiddleware<ApiKeyScopeMiddleware>(); // what an API key may reach (S28)
app.UseRateLimiter();
app.UseAuthorization();
app.UseResumableUploads(); // tus protocol under /api/uploads, S27
app.MapDefaultEndpoints();
app.MapAuthEndpoints();
app.MapAdminUserEndpoints();
app.MapAdminJobEndpoints();
app.MapOpenApi(); // GET /openapi/v1.json, behind the same fallback auth policy as everything else

// Map SignalR hub
app.MapHub<TranscriptionHub>("/hubs/transcription");

// Map API endpoints
app.MapAudioJobEndpoints();
app.MapResumableUploadEndpoints();
app.MapTranscriptionOptionsEndpoints();
app.MapTranscriptOutputEndpoints();
app.MapSegmentEditEndpoints();
app.MapExportEndpoints();
app.MapVariantEndpoints();
app.MapTemplateEndpoints();
app.MapChapterEndpoints();
app.MapSpeakerEndpoints();
app.MapSearchEndpoints();
app.MapSemanticSearchEndpoints();
app.MapChatEndpoints();
app.MapJobShareEndpoints();
app.MapJobOrganizationEndpoints();
app.MapGlossaryEndpoints();
app.MapQueueEndpoints();
app.MapApiKeyEndpoints();
app.MapWebhookEndpoints();

app.Run();
public partial class Program { }
