using AudioTranscription.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AudioTranscription.Api.Health;

// Readiness checks (S29-T4). They report a status and a fixed, generic description only: the health endpoint
// is meant for operators and probes, so connection strings, paths and host names never end up in a result.

/// <summary>The database answers.</summary>
public sealed class DatabaseHealthCheck(AppDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("Database reachable")
                : HealthCheckResult.Unhealthy("Database not reachable");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Database not reachable");
        }
    }
}

/// <summary>ffmpeg, needed for every conversion, is on the PATH (looked up, not started).</summary>
public sealed class FfmpegHealthCheck : IHealthCheck
{
    private readonly string? _path;

    /// <summary>Looks on the PATH of this process.</summary>
    public FfmpegHealthCheck() : this(Environment.GetEnvironmentVariable("PATH"))
    {
    }

    /// <summary>Looks on the given PATH (for tests); null or empty means nowhere.</summary>
    public FfmpegHealthCheck(string? path) => _path = path;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var fileName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var found = (_path ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, fileName)));

        return Task.FromResult(found
            ? HealthCheckResult.Healthy("ffmpeg found")
            : HealthCheckResult.Unhealthy("ffmpeg not found; audio cannot be converted"));
    }
}

/// <summary>
/// The default Whisper model is downloaded. A missing model is only <em>degraded</em>: it is fetched
/// automatically with the first job, so the service works, but that job will be slow.
/// </summary>
public sealed class WhisperModelHealthCheck(string modelsDirectory, string defaultModel) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var file = Path.Combine(modelsDirectory, $"ggml-{defaultModel.ToLowerInvariant()}.bin");
        return Task.FromResult(File.Exists(file)
            ? HealthCheckResult.Healthy("Default Whisper model present")
            : HealthCheckResult.Degraded("Default Whisper model not downloaded yet; the first job downloads it"));
    }
}

/// <summary>Ollama (post-processing) answers. Optional, so a problem is <em>degraded</em>, not unhealthy.</summary>
public sealed class OllamaHealthCheck(HttpClient httpClient, Uri baseAddress) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.GetAsync(new Uri(baseAddress, "/api/tags"), cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("Ollama reachable")
                : HealthCheckResult.Degraded("Ollama answered with an error; post-processing may fail");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Degraded("Ollama not reachable; post-processing is unavailable");
        }
    }
}
