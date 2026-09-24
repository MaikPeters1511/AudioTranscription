using AudioTranscription.Api.BackgroundServices;
using AudioTranscription.Api.Configuration;
using AudioTranscription.Api.Endpoints;
using AudioTranscription.Api.Hubs;
using AudioTranscription.Api.Storage;
using AudioTranscription.Infrastructure.Data;
using AudioTranscription.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

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
builder.Services.AddSingleton<ITempFileStore, TempFileStore>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ITranscriptionService, WhisperTranscriptionService>();
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

// Configure CORS for Angular frontend (SignalR needs AllowCredentials)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// Configure max request body size for file uploads
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 15_000_000; // ~15MB to allow overhead
});

var app = builder.Build();

// Ensure database is created via master connection (to avoid SQL Server Error 18456 State 38) and initialize schema
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

    await dbContext.Database.EnsureCreatedAsync();
}

app.UseCors();
app.MapDefaultEndpoints();

// Map SignalR hub
app.MapHub<TranscriptionHub>("/hubs/transcription");

// Map API endpoints
app.MapAudioJobEndpoints();

app.Run();
public partial class Program { }
