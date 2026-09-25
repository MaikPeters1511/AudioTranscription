using System.Net;
using System.Net.Http.Json;
using AudioTranscription.Api.BackgroundServices;
using AudioTranscription.Api.Configuration;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Api.Hubs;
using AudioTranscription.Api.Storage;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using AudioTranscription.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace AudioTranscription.Tests.Integration;

/// <summary>S09: cancel, delete and retry jobs through the API.</summary>
public class JobLifecycleIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly Mock<IClientProxy> _allClients = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public JobLifecycleIntegrationTests(WebApplicationFactory<Program> factory)
    {
        var dbName = Guid.NewGuid().ToString();
        var transcription = new Mock<ITranscriptionService>();
        transcription
            .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TranscriptionSettings>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranscriptionResult("Zweiter Versuch", "de", 1));
        var hubClients = new Mock<IHubClients>();
        hubClients.Setup(c => c.All).Returns(_allClients.Object);
        var hub = new Mock<IHubContext<TranscriptionHub>>();
        hub.Setup(h => h.Clients).Returns(hubClients.Object);

        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            var dbDescriptors = services.Where(d =>
                d.ServiceType == typeof(AppDbContext) ||
                d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                (d.ServiceType.IsGenericType && d.ServiceType.GetGenericArguments().Contains(typeof(AppDbContext))))
                .ToList();
            foreach (var d in dbDescriptors)
                services.Remove(d);
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));

            services.AddSingleton(transcription.Object);
            services.AddSingleton(hub.Object);
            services.AddTestAuthentication();
            services.Configure<UploadOptions>(o => o.TempStoragePath = _dir.Path);
        }));
        _client = _factory.CreateClient();
    }

    // --- cancel ---------------------------------------------------------------

    [Fact]
    public async Task Cancel_PendingJob_MarksItCancelled()
    {
        var job = await SeedAsync(AudioJobStatus.Pending);

        var response = await _client.PostAsync($"/api/audio-jobs/{job.Id}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var stored = await GetAsync(job.Id);
        stored!.Status.Should().Be(AudioJobStatus.Cancelled);
        stored.CompletedAtUtc.Should().NotBeNull();
        VerifyBroadcast("JobStatusChanged");
    }

    [Fact]
    public async Task Cancel_RunningJob_SignalsTheWorker()
    {
        var job = await SeedAsync(AudioJobStatus.Processing);
        using var running = Registry.Register(job.Id, CancellationToken.None);

        var response = await _client.PostAsync($"/api/audio-jobs/{job.Id}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        running.IsCancellationRequested.Should().BeTrue();
        Registry.Unregister(job.Id);
    }

    [Fact]
    public async Task Cancel_ProcessingJobThatIsNotRunning_MarksItCancelled()
    {
        var job = await SeedAsync(AudioJobStatus.Processing);

        var response = await _client.PostAsync($"/api/audio-jobs/{job.Id}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await GetAsync(job.Id))!.Status.Should().Be(AudioJobStatus.Cancelled);
    }

    [Theory]
    [InlineData(AudioJobStatus.Completed)]
    [InlineData(AudioJobStatus.Failed)]
    [InlineData(AudioJobStatus.Cancelled)]
    public async Task Cancel_FinishedJob_Returns409(AudioJobStatus status)
    {
        var job = await SeedAsync(status);

        var response = await _client.PostAsync($"/api/audio-jobs/{job.Id}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await GetAsync(job.Id))!.Status.Should().Be(status);
    }

    // --- delete ---------------------------------------------------------------

    [Fact]
    public async Task Delete_RemovesJobAndUpload()
    {
        var job = await SeedAsync(AudioJobStatus.Failed);

        var response = await _client.DeleteAsync($"/api/audio-jobs/{job.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.GetAsync($"/api/audio-jobs/{job.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        File.Exists(UploadPath(job)).Should().BeFalse();
        VerifyBroadcast("JobDeleted", job.Id);
    }

    [Fact]
    public async Task Delete_RunningJob_CancelsItFirst()
    {
        var job = await SeedAsync(AudioJobStatus.Processing);
        using var running = Registry.Register(job.Id, CancellationToken.None);

        var response = await _client.DeleteAsync($"/api/audio-jobs/{job.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        running.IsCancellationRequested.Should().BeTrue();
        (await GetAsync(job.Id)).Should().BeNull();
        Registry.Unregister(job.Id);
    }

    // --- progress (S07) -------------------------------------------------------

    [Fact]
    public async Task RunningJob_IncludesItsLatestProgressInListAndDetail()
    {
        var job = await SeedAsync(AudioJobStatus.Processing);
        var other = await SeedAsync(AudioJobStatus.Completed);
        var store = _factory.Services.GetRequiredService<JobProgressStore>();
        store.Set(job.Id, 42);
        store.Set(other.Id, 99); // stale value of a job that has just finished

        var detail = await _client.GetFromJsonAsync<AudioJobDto>($"/api/audio-jobs/{job.Id}");
        var list = await _client.GetFromJsonAsync<PaginatedResult<AudioJobListDto>>("/api/audio-jobs");

        detail!.ProgressPercent.Should().Be(42);
        list!.Items.Single(j => j.Id == job.Id).ProgressPercent.Should().Be(42);
        list.Items.Single(j => j.Id == other.Id).ProgressPercent.Should().BeNull();
    }

    // --- retry ----------------------------------------------------------------

    [Theory]
    [InlineData(AudioJobStatus.Failed)]
    [InlineData(AudioJobStatus.Cancelled)]
    public async Task Retry_ResetsAndReprocessesTheJob(AudioJobStatus status)
    {
        var job = await SeedAsync(status, errorMessage: "ffmpeg fehlt");

        var response = await _client.PostAsync($"/api/audio-jobs/{job.Id}/retry", null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var done = await WaitForAsync(job.Id, AudioJobStatus.Completed);
        done.RawTranscript.Should().Be("Zweiter Versuch");
        done.ErrorMessage.Should().BeNull();
        File.Exists(UploadPath(job)).Should().BeFalse("a successful retry cleans up the upload");
    }

    [Theory]
    [InlineData(AudioJobStatus.Pending)]
    [InlineData(AudioJobStatus.Processing)]
    [InlineData(AudioJobStatus.Completed)]
    public async Task Retry_JobThatIsNotFailedOrCancelled_Returns409(AudioJobStatus status)
    {
        var job = await SeedAsync(status);

        var response = await _client.PostAsync($"/api/audio-jobs/{job.Id}/retry", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Retry_WithoutUpload_Returns410()
    {
        var job = await SeedAsync(AudioJobStatus.Failed, withUpload: false);

        var response = await _client.PostAsync($"/api/audio-jobs/{job.Id}/retry", null);

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        (await GetAsync(job.Id))!.Status.Should().Be(AudioJobStatus.Failed);
    }

    // --- unknown jobs -----------------------------------------------------------

    [Theory]
    [InlineData("POST", "cancel")]
    [InlineData("POST", "retry")]
    [InlineData("DELETE", "")]
    public async Task UnknownJob_Returns404(string method, string action)
    {
        var url = $"/api/audio-jobs/{Guid.NewGuid()}" + (action.Length > 0 ? $"/{action}" : "");

        var response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- helpers ----------------------------------------------------------------

    private JobCancellationRegistry Registry => _factory.Services.GetRequiredService<JobCancellationRegistry>();

    private string UploadPath(AudioJob job) =>
        _factory.Services.GetRequiredService<ITempFileStore>().GetUploadPath(job.Id, job.FileName);

    private async Task<AudioJob> SeedAsync(AudioJobStatus status, string? errorMessage = null, bool withUpload = true)
    {
        var job = new AudioJob
        {
            FileName = "meeting.mp3",
            ContentType = "audio/mpeg",
            Status = status,
            ErrorMessage = errorMessage,
            RawTranscript = status == AudioJobStatus.Completed ? "fertig" : null,
            CompletedAtUtc = status is AudioJobStatus.Pending or AudioJobStatus.Processing ? null : DateTime.UtcNow,
        };
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AudioJobs.Add(job);
        await db.SaveChangesAsync();
        if (withUpload)
            _dir.CreateFile(Path.GetFileName(UploadPath(job)));
        return job;
    }

    private async Task<AudioJob?> GetAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AudioJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id);
    }

    private async Task<AudioJob> WaitForAsync(Guid id, AudioJobStatus expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        AudioJob? job;
        do
        {
            job = await GetAsync(id);
            if (job?.Status == expected)
                return job;
            await Task.Delay(50);
        } while (DateTime.UtcNow < deadline);

        job!.Status.Should().Be(expected, "(last error: {0})", job.ErrorMessage ?? "none");
        return job;
    }

    private void VerifyBroadcast(string method, Guid? jobId = null) =>
        _allClients.Verify(c => c.SendCoreAsync(
            method,
            It.Is<object?[]>(args => jobId == null || (args.Length == 1 && args[0] is Guid && (Guid)args[0]! == jobId)),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        _dir.Dispose();
    }
}
