using System.Net;
using AudioTranscription.Api.Health;
using AudioTranscription.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AudioTranscription.Tests.Telemetry;

/// <summary>S29-T4: readiness checks for database, ffmpeg, the Whisper model and (optionally) Ollama.</summary>
public class HealthChecksTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private static Task<HealthCheckResult> Run(IHealthCheck check) =>
        check.CheckHealthAsync(new HealthCheckContext { Registration = new HealthCheckRegistration("test", check, null, null) });

    // ---- database

    [Fact]
    public async Task Database_IsHealthy_WhenItCanBeReached()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);

        (await Run(new DatabaseHealthCheck(db))).Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Database_IsUnhealthy_WhenItCannotBeReached_AndTheResultRevealsNothing()
    {
        var password = TestCredentials.NewPassword(); // generated: no password-like literal in the repository
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer($"Server=127.0.0.1,1;Database=secretdb;User Id=sa;Password={password};Connect Timeout=1;TrustServerCertificate=True")
            .Options;
        await using var db = new AppDbContext(options);

        var result = await Run(new DatabaseHealthCheck(db));

        result.Status.Should().Be(HealthStatus.Unhealthy);
        AllText(result).Should().NotContain("secretdb").And.NotContain(password).And.NotContain("127.0.0.1");
    }

    // ---- ffmpeg

    [Fact]
    public async Task Ffmpeg_IsHealthy_WhenTheBinaryIsOnThePath()
    {
        _dir.CreateFile(OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");

        (await Run(new FfmpegHealthCheck(path: $"/nonexistent{Path.PathSeparator}{_dir.Path}"))).Status.Should().Be(HealthStatus.Healthy);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("/nonexistent")]
    public async Task Ffmpeg_IsUnhealthy_WhenItIsNotFound(string? path)
    {
        var result = await Run(new FfmpegHealthCheck(path));

        result.Status.Should().Be(HealthStatus.Unhealthy);
        AllText(result).Should().NotContain("/nonexistent");
    }

    // ---- Whisper model

    [Fact]
    public async Task WhisperModel_IsHealthy_WhenTheDefaultModelIsDownloaded()
    {
        _dir.CreateFile("ggml-base.bin");

        (await Run(new WhisperModelHealthCheck(_dir.Path, "Base"))).Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task WhisperModel_IsOnlyDegraded_WhenItWillBeDownloadedOnFirstUse()
    {
        var result = await Run(new WhisperModelHealthCheck(_dir.Path, "Base"));

        result.Status.Should().Be(HealthStatus.Degraded, "the model is fetched automatically with the first job, the service works");
        AllText(result).Should().NotContain(_dir.Path);
    }

    // ---- Ollama

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(respond(request));
        }
    }

    [Fact]
    public async Task Ollama_IsHealthy_WhenItAnswers_AskingOnlyForItsModelList()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await Run(new OllamaHealthCheck(new HttpClient(handler), new Uri("http://ollama.internal:11434")));

        result.Status.Should().Be(HealthStatus.Healthy);
        handler.Request!.Method.Should().Be(HttpMethod.Get);
        handler.Request.RequestUri!.AbsolutePath.Should().Be("/api/tags");
    }

    [Fact]
    public async Task Ollama_IsDegraded_WhenItAnswersWithAnError_AsTheTranscriptionStillWorks()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        (await Run(new OllamaHealthCheck(new HttpClient(handler), new Uri("http://ollama.internal:11434")))).Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task Ollama_IsDegraded_WhenItCannotBeReached_AndTheResultRevealsNothing()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("Connection refused (ollama.internal:11434)"));

        var result = await Run(new OllamaHealthCheck(new HttpClient(handler), new Uri("http://ollama.internal:11434")));

        result.Status.Should().Be(HealthStatus.Degraded);
        AllText(result).Should().NotContain("ollama.internal");
        result.Exception.Should().BeNull();
    }

    private static string AllText(HealthCheckResult result) =>
        $"{result.Description} {result.Exception?.Message} {string.Join(' ', result.Data.Select(d => $"{d.Key}={d.Value}"))}";
}
