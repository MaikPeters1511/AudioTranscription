using System.Net;
using System.Net.Http.Json;
using AudioTranscription.Api.Configuration;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AudioTranscription.Tests.Integration;

/// <summary>S30: the owner can exclude a job from automatic deletion; the job shows when it will be deleted.</summary>
public class RetentionLockIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string OtherUser = "someone-else";
    private readonly WebApplicationFactory<Program> _factory;

    public RetentionLockIntegrationTests(WebApplicationFactory<Program> factory)
    {
        var dbName = Guid.NewGuid().ToString();
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
            services.AddTestAuthentication();
            services.Configure<RetentionOptions>(o => o.TranscriptDays = 30);
        }));
    }

    private HttpClient Client(string? userId = null)
    {
        var client = _factory.CreateClient();
        if (userId is not null)
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId);
        return client;
    }

    private async Task<Guid> AddJobAsync(
        string? ownerId = null,
        AudioJobStatus status = AudioJobStatus.Completed,
        bool locked = false,
        string? sharedWith = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = new AudioJob
        {
            FileName = "meeting.mp3",
            ContentType = "audio/mpeg",
            Status = status,
            OwnerId = ownerId ?? TestAuthHandler.DefaultUserId,
            RetentionLocked = locked,
            CreatedAtUtc = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc),
            CompletedAtUtc = new DateTime(2026, 1, 10, 8, 0, 0, DateTimeKind.Utc),
        };
        db.AudioJobs.Add(job);
        if (sharedWith is not null)
            db.JobShares.Add(new JobShare { AudioJobId = job.Id, UserId = sharedWith });
        await db.SaveChangesAsync();
        return job.Id;
    }

    private async Task<AudioJob> LoadAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AudioJobs.AsNoTracking().SingleAsync(j => j.Id == id);
    }

    [Fact]
    public async Task Owner_CanLockAndUnlockAJob()
    {
        var id = await AddJobAsync();

        (await Client().PutAsJsonAsync($"/api/audio-jobs/{id}/retention-lock", new { locked = true })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await LoadAsync(id)).RetentionLocked.Should().BeTrue();

        (await Client().PutAsJsonAsync($"/api/audio-jobs/{id}/retention-lock", new { locked = false })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await LoadAsync(id)).RetentionLocked.Should().BeFalse();
    }

    [Fact]
    public async Task SharedUser_CannotLock_AndOthersDoNotSeeTheJob()
    {
        var id = await AddJobAsync(ownerId: OtherUser, sharedWith: TestAuthHandler.DefaultUserId);
        var foreign = await AddJobAsync(ownerId: OtherUser);

        (await Client().PutAsJsonAsync($"/api/audio-jobs/{id}/retention-lock", new { locked = true })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Client().PutAsJsonAsync($"/api/audio-jobs/{foreign}/retention-lock", new { locked = true })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await LoadAsync(id)).RetentionLocked.Should().BeFalse();
    }

    [Fact]
    public async Task Detail_ShowsWhenTheJobWillBeDeleted()
    {
        var id = await AddJobAsync();

        var job = await Client().GetFromJsonAsync<AudioJobDto>($"/api/audio-jobs/{id}");

        job!.RetentionLocked.Should().BeFalse();
        job.DeleteAtUtc.Should().Be(new DateTime(2026, 2, 9, 8, 0, 0, DateTimeKind.Utc), "completion time plus 30 days");
    }

    [Fact]
    public async Task Detail_OfALockedJob_HasNoDeletionDate()
    {
        var id = await AddJobAsync(locked: true);

        var job = await Client().GetFromJsonAsync<AudioJobDto>($"/api/audio-jobs/{id}");

        job!.RetentionLocked.Should().BeTrue();
        job.DeleteAtUtc.Should().BeNull();
    }

    [Theory]
    [InlineData(AudioJobStatus.Pending)]
    [InlineData(AudioJobStatus.Processing)]
    public async Task Detail_OfAnOpenJob_HasNoDeletionDate(AudioJobStatus status)
    {
        var id = await AddJobAsync(status: status);

        var job = await Client().GetFromJsonAsync<AudioJobDto>($"/api/audio-jobs/{id}");

        job!.DeleteAtUtc.Should().BeNull();
    }
}
