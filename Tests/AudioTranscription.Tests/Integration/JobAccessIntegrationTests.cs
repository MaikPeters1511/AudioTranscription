using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AudioTranscription.Api.Access;
using AudioTranscription.Api.Configuration;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Api.Hubs;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using AudioTranscription.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace AudioTranscription.Tests.Integration;

/// <summary>
/// S19 / ADR 0007: jobs belong to the user who uploaded them, can be shared read-only and are
/// invisible (404) to everyone else, admins included.
/// </summary>
public class JobAccessIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly Mock<IClientProxy> _userClients = new();
    private readonly List<IReadOnlyList<string>> _notifiedUsers = [];

    private string _alice = null!;
    private string _bob = null!;
    private string _carol = null!;
    private string _admin = null!;
    private const string AliceEmail = "alice@example.com";
    private const string BobEmail = "bob@example.com";

    public JobAccessIntegrationTests(WebApplicationFactory<Program> factory)
    {
        var dbName = Guid.NewGuid().ToString();
        var transcription = new Mock<ITranscriptionService>();
        transcription
            .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TranscriptionSettings>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranscriptionResult("Hallo", "de", 1));
        var hubClients = new Mock<IHubClients>();
        hubClients.Setup(c => c.Users(It.IsAny<IReadOnlyList<string>>()))
            .Callback<IReadOnlyList<string>>(users => _notifiedUsers.Add(users))
            .Returns(_userClients.Object);
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
            services.Configure<UploadOptions>(o =>
            {
                o.TempStoragePath = _dir.Path;
                o.DeleteAfterTranscription = false;
            });
        }));
        SeedUsersAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _dir.Dispose();

    private async Task SeedUsersAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        async Task<string> Create(string email)
        {
            var user = new IdentityUser { UserName = email, Email = email };
            (await users.CreateAsync(user, TestCredentials.NewPassword())).Succeeded.Should().BeTrue();
            return user.Id;
        }
        _alice = await Create(AliceEmail);
        _bob = await Create(BobEmail);
        _carol = await Create("carol@example.com");
        _admin = await Create("admin@example.com");
    }

    private HttpClient As(string userId, string? roles = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId);
        if (roles is not null)
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, roles);
        return client;
    }

    private async Task<Guid> AddJobAsync(string? ownerId, string fileName = "meeting.mp3", AudioJobStatus status = AudioJobStatus.Completed, params string[] sharedWith)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = new AudioJob
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            FileSizeBytes = 10,
            ContentType = "audio/mpeg",
            Status = status,
            RawTranscript = "Hallo Welt",
            OwnerId = ownerId,
            DiarizationRequested = true,
        };
        db.AudioJobs.Add(job);
        db.TranscriptSegments.Add(new TranscriptSegment { AudioJobId = job.Id, Index = 0, StartMs = 0, EndMs = 1000, Text = "Hallo Welt", SpeakerIndex = 0 });
        db.JobSpeakers.Add(new JobSpeaker { AudioJobId = job.Id, Index = 0 });
        foreach (var user in sharedWith)
            db.JobShares.Add(new JobShare { AudioJobId = job.Id, UserId = user });
        await db.SaveChangesAsync();
        return job.Id;
    }

    // ---------------------------------------------------------------- visibility

    [Fact]
    public async Task List_ShowsOnlyOwnAndSharedJobs()
    {
        var own = await AddJobAsync(_bob, "own.mp3");
        var shared = await AddJobAsync(_alice, "shared.mp3", sharedWith: _bob);
        await AddJobAsync(_alice, "private.mp3");
        await AddJobAsync(null, "unassigned.mp3");

        var page = await As(_bob).GetFromJsonAsync<PaginatedResult<AudioJobListDto>>("/api/audio-jobs");

        page!.TotalCount.Should().Be(2);
        page.Items.Select(j => j.Id).Should().BeEquivalentTo([own, shared]);
        page.Items.Single(j => j.Id == own).IsOwner.Should().BeTrue();
        var sharedItem = page.Items.Single(j => j.Id == shared);
        sharedItem.IsOwner.Should().BeFalse();
        sharedItem.SharedByEmail.Should().Be(AliceEmail);
    }

    [Fact]
    public async Task Detail_IsVisibleToOwnerAndSharedUser_ButNotToOthers_AdminsIncluded()
    {
        var id = await AddJobAsync(_alice, sharedWith: _bob);

        (await As(_alice).GetAsync($"/api/audio-jobs/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await As(_bob).GetAsync($"/api/audio-jobs/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await As(_carol).GetAsync($"/api/audio-jobs/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await As(_admin, "Admin").GetAsync($"/api/audio-jobs/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("GET", "")]
    [InlineData("GET", "/segments")]
    [InlineData("GET", "/subtitles?format=srt")]
    [InlineData("GET", "/audio")]
    [InlineData("GET", "/variants")]
    [InlineData("GET", "/speakers")]
    [InlineData("DELETE", "")]
    [InlineData("POST", "/cancel")]
    [InlineData("POST", "/retry")]
    [InlineData("PUT", "/speakers/0")]
    [InlineData("GET", "/shares")]
    public async Task EveryJobRoute_Returns404_ForSomeoneElsesJob(string method, string suffix)
    {
        var id = await AddJobAsync(_alice);

        var body = new { displayName = "x", mode = 0, email = "bob@example.com" };

        var response = await Send(As(_carol), method, $"/api/audio-jobs/{id}{suffix}", body);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        // The same answer as for a job that does not exist, so the id is not confirmed
        (await Send(As(_carol), method, $"/api/audio-jobs/{Guid.NewGuid()}{suffix}", body)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SharedUser_CanRead_SegmentsVariantsAndSpeakers()
    {
        var id = await AddJobAsync(_alice, sharedWith: _bob);
        var bob = As(_bob);

        (await bob.GetAsync($"/api/audio-jobs/{id}/segments")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await bob.GetAsync($"/api/audio-jobs/{id}/variants")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await bob.GetAsync($"/api/audio-jobs/{id}/speakers")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await bob.GetAsync($"/api/audio-jobs/{id}/subtitles?format=srt")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("DELETE", "")]
    [InlineData("POST", "/cancel")]
    [InlineData("POST", "/retry")]
    [InlineData("POST", "/variants")]
    [InlineData("PUT", "/speakers/0")]
    [InlineData("GET", "/shares")]
    [InlineData("POST", "/shares")]
    public async Task SharedUser_CannotChangeTheJob_NorManageShares(string method, string suffix)
    {
        var id = await AddJobAsync(_alice, status: AudioJobStatus.Failed, sharedWith: _bob);

        var response = await Send(As(_bob), method, $"/api/audio-jobs/{id}{suffix}", new { displayName = "x", mode = 0, email = "carol@example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var scope = _factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().AudioJobs.AnyAsync(j => j.Id == id)).Should().BeTrue();
    }

    [Fact]
    public async Task Owner_CanDeleteTheJob()
    {
        var id = await AddJobAsync(_alice);

        (await As(_alice).DeleteAsync($"/api/audio-jobs/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ---------------------------------------------------------------- upload

    [Fact]
    public async Task Upload_MakesTheCurrentUserTheOwner()
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent([.. "ID3"u8.ToArray(), 0, 0, 0, 0]);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        content.Add(file, "file", "new.mp3");

        var response = await As(_bob).PostAsync("/api/audio-jobs", content);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var created = await response.Content.ReadFromJsonAsync<CreateAudioJobResponse>();
        using var scope = _factory.Services.CreateScope();
        var job = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AudioJobs.SingleAsync(j => j.Id == created!.Id);
        job.OwnerId.Should().Be(_bob);
        (await As(_alice).GetAsync($"/api/audio-jobs/{created!.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Upload_WithoutAUser_IsRejected_AndNothingIsStoredOrQueued()
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent([.. "ID3"u8.ToArray(), 0, 0, 0, 0]);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        content.Add(file, "file", "anonymous.mp3");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, TestAuthHandler.NoUser); // authenticated, but without a user id

        var response = await client.PostAsync("/api/audio-jobs", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var scope = _factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().AudioJobs.AnyAsync(j => j.FileName == "anonymous.mp3")).Should().BeFalse();
        Directory.GetFiles(_dir.Path, "*", SearchOption.AllDirectories).Should().BeEmpty("the upload must not even be written to disk");
    }

    // ---------------------------------------------------------------- coverage (M1)

    [Fact]
    public void EveryRouteBelowAJobId_IsGuardedByTheAccessFilter()
    {
        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } raw && raw.StartsWith("/api/audio-jobs/{id", StringComparison.Ordinal))
            .ToList();

        endpoints.Should().NotBeEmpty();
        endpoints.Where(e => e.Metadata.GetMetadata<JobAccessGuardMetadata>() is null)
            .Select(e => $"{e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.FirstOrDefault()} {e.RoutePattern.RawText}")
            .Should().BeEmpty("a route that is not guarded would expose other users' jobs");
    }

    // ---------------------------------------------------------------- shares

    [Fact]
    public async Task Owner_CanShareByEmail_ListAndRevoke()
    {
        var id = await AddJobAsync(_alice);
        var alice = As(_alice);

        var share = await alice.PostAsJsonAsync($"/api/audio-jobs/{id}/shares", new { email = BobEmail });
        share.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await As(_bob).GetAsync($"/api/audio-jobs/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var shares = await alice.GetFromJsonAsync<List<JobShareDto>>($"/api/audio-jobs/{id}/shares");
        shares.Should().ContainSingle().Which.Should().Match<JobShareDto>(s => s.UserId == _bob && s.Email == BobEmail);

        (await alice.DeleteAsync($"/api/audio-jobs/{id}/shares/{_bob}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await As(_bob).GetAsync($"/api/audio-jobs/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sharing_Twice_IsIdempotent()
    {
        var id = await AddJobAsync(_alice);
        var alice = As(_alice);

        (await alice.PostAsJsonAsync($"/api/audio-jobs/{id}/shares", new { email = BobEmail })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await alice.PostAsJsonAsync($"/api/audio-jobs/{id}/shares", new { email = BobEmail })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await alice.GetFromJsonAsync<List<JobShareDto>>($"/api/audio-jobs/{id}/shares"))!.Should().HaveCount(1);
    }

    [Theory]
    [InlineData(AliceEmail)]
    [InlineData("nobody@example.com")]
    [InlineData("")]
    public async Task Sharing_WithYourselfOrAnUnknownUser_IsRejected(string email)
    {
        var id = await AddJobAsync(_alice);

        var response = await As(_alice).PostAsJsonAsync($"/api/audio-jobs/{id}/shares", new { email });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------- admin

    [Fact]
    public async Task Admin_CanListAndAssignUnassignedJobs()
    {
        var orphan = await AddJobAsync(null, "orphan.mp3");
        await AddJobAsync(_alice, "owned.mp3");
        var admin = As(_admin, "Admin");

        var unassigned = await admin.GetFromJsonAsync<List<AudioJobListDto>>("/api/admin/jobs/unassigned");
        unassigned!.Select(j => j.Id).Should().Equal(orphan);

        (await admin.PostAsJsonAsync($"/api/admin/jobs/{orphan}/owner", new { userId = _bob })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await As(_bob).GetAsync($"/api/audio-jobs/{orphan}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetFromJsonAsync<List<AudioJobListDto>>("/api/admin/jobs/unassigned"))!.Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentAssignments_GiveAnUnassignedJobToExactlyOneUser()
    {
        var orphan = await AddJobAsync(null);
        var first = As(_admin, "Admin");
        var second = As(_admin, "Admin");

        var results = await Task.WhenAll(
            first.PostAsJsonAsync($"/api/admin/jobs/{orphan}/owner", new { userId = _alice }),
            second.PostAsJsonAsync($"/api/admin/jobs/{orphan}/owner", new { userId = _bob }));

        results.Count(r => r.StatusCode == HttpStatusCode.NoContent).Should().Be(1);
        results.Count(r => r.StatusCode == HttpStatusCode.NotFound).Should().Be(1);
        var winner = results[0].StatusCode == HttpStatusCode.NoContent ? _alice : _bob;
        using var scope = _factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().AudioJobs.SingleAsync(j => j.Id == orphan)).OwnerId.Should().Be(winner);
    }

    [Fact]
    public async Task AdminJobEndpoints_AreForbiddenForNonAdmins()
    {
        var orphan = await AddJobAsync(null);

        (await As(_alice).GetAsync("/api/admin/jobs/unassigned")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await As(_alice).PostAsJsonAsync($"/api/admin/jobs/{orphan}/owner", new { userId = _alice })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_CannotAssignAnOwnerThatDoesNotExist_AndSeesOwnedJobsAsNonExistent()
    {
        var orphan = await AddJobAsync(null);
        var owned = await AddJobAsync(_alice);
        var admin = As(_admin, "Admin");

        (await admin.PostAsJsonAsync($"/api/admin/jobs/{orphan}/owner", new { userId = "no-such-user" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // Not 409: that would confirm to an admin that a job of someone else exists
        (await admin.PostAsJsonAsync($"/api/admin/jobs/{owned}/owner", new { userId = _bob })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await As(_alice).GetAsync($"/api/audio-jobs/{owned}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---------------------------------------------------------------- live updates

    [Fact]
    public async Task Deleting_NotifiesOnlyOwnerAndSharedUsers()
    {
        var id = await AddJobAsync(_alice, sharedWith: _bob);
        await AddJobAsync(_carol);

        (await As(_alice).DeleteAsync($"/api/audio-jobs/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        _notifiedUsers.Should().ContainSingle();
        _notifiedUsers[0].Should().BeEquivalentTo([_alice, _bob]);
        _userClients.Verify(c => c.SendCoreAsync("JobDeleted", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------------------------------------------------------------- helpers

    private static Task<HttpResponseMessage> Send(HttpClient client, string method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (body is not null && method is "POST" or "PUT")
            request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }
}
