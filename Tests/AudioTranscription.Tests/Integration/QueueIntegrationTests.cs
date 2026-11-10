using System.Net;
using System.Net.Http.Json;
using AudioTranscription.Api.BackgroundServices;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AudioTranscription.Tests.Integration;

/// <summary>S34: queue overview and priority of waiting jobs.</summary>
public class QueueIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _alice;
    private readonly string _bob;

    public QueueIntegrationTests(WebApplicationFactory<Program> factory)
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

            // The worker would drain the queue under the test's feet
            foreach (var d in services.Where(d =>
                         d.ServiceType == typeof(IHostedService) &&
                         d.ImplementationType == typeof(TranscriptionWorker)).ToList())
                services.Remove(d);
        }));
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        async Task<string> Create(string email)
        {
            var user = new IdentityUser { UserName = email, Email = email };
            (await users.CreateAsync(user, TestCredentials.NewPassword())).Succeeded.Should().BeTrue();
            return user.Id;
        }
        _alice = Create("alice@example.com").GetAwaiter().GetResult();
        _bob = Create("bob@example.com").GetAwaiter().GetResult();
    }

    private HttpClient As(string userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId);
        return client;
    }

    private async Task<Guid> AddQueuedJobAsync(
        string ownerId, long bytes = 1_000_000, int priority = 0,
        AudioJobStatus status = AudioJobStatus.Pending, bool enqueue = true)
    {
        var id = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AudioJobs.Add(new AudioJob
            {
                Id = id, FileName = $"{id}.mp3", FileSizeBytes = bytes, ContentType = "audio/mpeg",
                Status = status, Model = "Base", OwnerId = ownerId, Priority = priority,
                QueuedAtUtc = DateTime.UtcNow,
                StartedAtUtc = status == AudioJobStatus.Processing ? DateTime.UtcNow : null
            });
            await db.SaveChangesAsync();
        }
        if (enqueue)
        {
            await _factory.Services.GetRequiredService<TranscriptionQueue>()
                .EnqueueAsync(new TranscriptionJobRequest(id, $"{id}.mp3", priority));
        }
        return id;
    }

    private void ClearQueue()
    {
        var queue = _factory.Services.GetRequiredService<TranscriptionQueue>();
        while (queue.TryRead(out _)) { }
    }

    private async Task<QueueDto> GetQueueAsync(string userId) =>
        (await As(userId).GetFromJsonAsync<QueueDto>("/api/queue"))!;

    private static SetPriorityRequest Priority(int? value) => new(value);

    [Fact]
    public async Task Queue_ListsOwnWaitingJobsWithPositionInProcessingOrder()
    {
        ClearQueue();
        var first = await AddQueuedJobAsync(_alice);
        await AddQueuedJobAsync(_bob);
        var third = await AddQueuedJobAsync(_alice);

        var queue = await GetQueueAsync(_alice);

        // Bob's job takes position 2 but is not listed; Alice's positions count it
        queue.Entries.Select(e => (e.JobId, e.Position)).Should().Equal((first, 1), (third, 3));
        queue.TotalWaiting.Should().Be(3);
    }

    [Fact]
    public async Task Queue_ShowsTheRunningJobAtPositionZero()
    {
        ClearQueue();
        var running = await AddQueuedJobAsync(_alice, status: AudioJobStatus.Processing, enqueue: false);
        var waiting = await AddQueuedJobAsync(_alice);

        var queue = await GetQueueAsync(_alice);

        queue.Entries.Select(e => (e.JobId, e.Position, e.Status)).Should()
            .Equal((running, 0, AudioJobStatus.Processing), (waiting, 1, AudioJobStatus.Pending));
    }

    [Fact]
    public async Task Queue_DoesNotShowJobsOfOtherUsers()
    {
        ClearQueue();
        await AddQueuedJobAsync(_bob);

        (await GetQueueAsync(_alice)).Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Queue_EstimatesAreEmptyWithoutFinishedJobs()
    {
        ClearQueue();
        await AddQueuedJobAsync(_alice);
        await AddQueuedJobAsync(_alice);

        var entries = (await GetQueueAsync(_alice)).Entries;

        // Nothing is ahead of the first job, so it starts at once even without a known speed
        entries[0].EstimatedWaitSeconds.Should().Be(0);
        entries[0].EstimatedProcessingSeconds.Should().BeNull();
        entries[1].EstimatedWaitSeconds.Should().BeNull();
    }

    [Fact]
    public async Task Queue_EstimatesFromTheSpeedOfFinishedJobs()
    {
        ClearQueue();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var start = DateTime.UtcNow.AddHours(-1);
            db.AudioJobs.Add(new AudioJob
            {
                FileName = "done.mp3", FileSizeBytes = 1_000_000, ContentType = "audio/mpeg",
                Status = AudioJobStatus.Completed, Model = "Base", OwnerId = _bob,
                StartedAtUtc = start, CompletedAtUtc = start.AddSeconds(100)
            });
            await db.SaveChangesAsync();
        }
        await AddQueuedJobAsync(_alice, bytes: 1_000_000);
        await AddQueuedJobAsync(_alice, bytes: 2_000_000);

        var entries = (await GetQueueAsync(_alice)).Entries;

        // 100 s per 1 MB
        entries[0].EstimatedWaitSeconds.Should().BeApproximately(0, 1);
        entries[0].EstimatedProcessingSeconds.Should().BeApproximately(100, 1);
        entries[1].EstimatedWaitSeconds.Should().BeApproximately(100, 1);
        entries[1].EstimatedProcessingSeconds.Should().BeApproximately(200, 1);
    }

    [Fact]
    public async Task SetPriority_MovesTheJobToTheFront()
    {
        ClearQueue();
        await AddQueuedJobAsync(_alice);
        var second = await AddQueuedJobAsync(_alice);

        var response = await As(_alice).PutAsJsonAsync($"/api/audio-jobs/{second}/priority", Priority(5));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var entries = (await GetQueueAsync(_alice)).Entries;
        entries.First().JobId.Should().Be(second);
        entries.First().Priority.Should().Be(5);
        entries.First().Position.Should().Be(1);
    }

    [Fact]
    public async Task SetPriority_IsStoredOnTheJob()
    {
        ClearQueue();
        var id = await AddQueuedJobAsync(_alice);

        await As(_alice).PutAsJsonAsync($"/api/audio-jobs/{id}/priority", Priority(3));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AudioJobs.FindAsync(id))!.Priority.Should().Be(3);
    }

    [Fact]
    public async Task SetPriority_OnAForeignJobReturns404()
    {
        var id = await AddQueuedJobAsync(_bob);

        var response = await As(_alice).PutAsJsonAsync($"/api/audio-jobs/{id}/priority", Priority(1));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(AudioJobStatus.Processing)]
    [InlineData(AudioJobStatus.Completed)]
    [InlineData(AudioJobStatus.Failed)]
    [InlineData(AudioJobStatus.Cancelled)]
    public async Task SetPriority_OnAJobThatIsNotWaitingReturns409(AudioJobStatus status)
    {
        var id = await AddQueuedJobAsync(_alice, status: status, enqueue: false);

        var response = await As(_alice).PutAsJsonAsync($"/api/audio-jobs/{id}/priority", Priority(1));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-101)]
    [InlineData(101)]
    public async Task SetPriority_WithMissingOrOutOfRangeValueReturns400(int? value)
    {
        var id = await AddQueuedJobAsync(_alice);

        var response = await As(_alice).PutAsJsonAsync($"/api/audio-jobs/{id}/priority", Priority(value));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CancelledWaitingJobDisappearsFromTheQueueView()
    {
        ClearQueue();
        var id = await AddQueuedJobAsync(_alice);
        await AddQueuedJobAsync(_alice);

        (await As(_alice).PostAsync($"/api/audio-jobs/{id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        var entries = (await GetQueueAsync(_alice)).Entries;
        entries.Should().ContainSingle();
        entries.Single().Position.Should().Be(1);
    }
}
