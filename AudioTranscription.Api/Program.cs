using AudioTranscription.Api.Auth;
using AudioTranscription.Api.BackgroundServices;
using AudioTranscription.Api.Configuration;
using AudioTranscription.Api.Endpoints;
using AudioTranscription.Api.Hubs;
using AudioTranscription.Api.Storage;
using AudioTranscription.Infrastructure.Data;
using AudioTranscription.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
builder.Services.AddSingleton<JobCancellationRegistry>();
builder.Services.AddSingleton<ITempFileStore, TempFileStore>();
builder.Services.AddSingleton(TimeProvider.System);
// Singleton: loaded Whisper models are kept and shared between jobs
builder.Services.AddOptions<WhisperOptions>()
    .Bind(builder.Configuration.GetSection(WhisperOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<WhisperOptions>, WhisperOptionsValidator>();
builder.Services.AddSingleton<ITranscriptionService, WhisperTranscriptionService>();
builder.Services.AddHostedService<InitialUserSeeder>();
builder.Services.AddHostedService<OrphanedUploadCleanupService>();
builder.Services.AddHostedService<JobRecoveryService>(); // must start before the worker
builder.Services.AddHostedService<TranscriptionWorker>();

// Optional: Register Ollama post-processor if connection string is present
var ollamaConnectionString = builder.Configuration.GetConnectionString("llama3.2");
if (!string.IsNullOrWhiteSpace(ollamaConnectionString))
{
    builder.Services.AddSingleton<Microsoft.Extensions.AI.IChatClient>(new Microsoft.Extensions.AI.OllamaChatClient(new Uri(ollamaConnectionString), "llama3.2"));
    builder.Services.AddScoped<ITranscriptPostProcessor, OllamaPostProcessor>();
}

// Add SignalR
builder.Services.AddSignalR();

// Authentication: local ASP.NET Core Identity with cookie sessions (ADR 0003)
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.AddIdentityApiEndpoints<IdentityUser>()
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

// Every endpoint requires a signed-in user unless it explicitly allows anonymous access
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

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

// Configure max request body size for file uploads
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 15_000_000; // ~15MB to allow overhead
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
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapAuthEndpoints();

// Map SignalR hub
app.MapHub<TranscriptionHub>("/hubs/transcription");

// Map API endpoints
app.MapAudioJobEndpoints();
app.MapTranscriptionOptionsEndpoints();

app.Run();
public partial class Program { }
