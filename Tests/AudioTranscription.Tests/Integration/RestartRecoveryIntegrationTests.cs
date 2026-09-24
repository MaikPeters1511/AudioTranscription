using AudioTranscription.Api.Configuration;
using AudioTranscription.Api.Storage;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using AudioTranscription.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace AudioTranscription.Tests.Integration;

/// <summary>
/// Simulates an API restart: the database already contains a job that was interrupted while
/// Processing and its upload is still on disk. Starting the host must finish that job.
/// </summary>
public class RestartRecoveryIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly InMemoryDatabaseRoot _dbRoot = new();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly WebApplicationFactory<Program> _factory;

    public RestartRecoveryIntegrationTests(WebApplicationFactory<Program> factory)
    {
        var transcription = new Mock<ITranscriptionService>();
        transcription
            .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranscriptionResult("Nach dem Neustart fertig", "de", 2));

        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            var dbDescriptors = services.Where(d =>
                d.ServiceType == typeof(AppDbContext) ||
                d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                (d.ServiceType.IsGenericType && d.ServiceType.GetGenericArguments().Contains(typeof(AppDbContext))))
                .ToList();
            foreach (var d in dbDescriptors)
                services.Remove(d);
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName, _dbRoot));

            services.AddSingleton(transcription.Object);
            services.Configure<UploadOptions>(o => o.TempStoragePath = _dir.Path);
        }));
    }

    [Fact]
    public async Task Startup_CompletesJobInterruptedByPreviousShutdown()
    {
        // Arrange: state left behind by a crashed/stopped API instance
        var job = new AudioJob
        {
            FileName = "meeting.mp3",
            ContentType = "audio/mpeg",
            Status = AudioJobStatus.Processing,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1)
        };
        await using (var db = CreateDbContext())
        {
            db.AudioJobs.Add(job);
            await db.SaveChangesAsync();
        }
        var store = new TempFileStore(Options.Create(new UploadOptions { TempStoragePath = _dir.Path }));
        var upload = _dir.CreateFile(Path.GetFileName(store.GetUploadPath(job.Id, job.FileName)));

        // Act: "restart" the API
        _ = _factory.Services;

        // Assert
        var finished = await WaitForStatusAsync(job.Id, AudioJobStatus.Completed, TimeSpan.FromSeconds(10));
        finished.RawTranscript.Should().Be("Nach dem Neustart fertig");
        File.Exists(upload).Should().BeFalse("the upload is deleted after successful processing");
    }

    private AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName, _dbRoot).Options);

    private async Task<AudioJob> WaitForStatusAsync(Guid jobId, AudioJobStatus expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        AudioJob job;
        do
        {
            await using var db = CreateDbContext();
            job = await db.AudioJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
            if (job.Status == expected)
                return job;
            await Task.Delay(50);
        } while (DateTime.UtcNow < deadline);

        job.Status.Should().Be(expected,
            "the recovered job should be processed within {0} (last error: {1})", timeout, job.ErrorMessage ?? "none");
        return job;
    }

    public void Dispose()
    {
        _factory.Dispose();
        _dir.Dispose();
    }
}
