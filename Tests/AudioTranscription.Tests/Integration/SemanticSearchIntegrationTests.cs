using System.Net;
using System.Net.Http.Json;
using AudioTranscription.Api.Auth;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Api.Search;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using AudioTranscription.Infrastructure.Embeddings;
using AudioTranscription.Tests.Search;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AudioTranscription.Tests.Integration;

/// <summary>S23: semantic search endpoint with the permission filter, the modes and the admin rebuild.</summary>
public class SemanticSearchIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string Other = "other-user";
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly FakeEmbeddingService _embeddings = new();
    private readonly InMemoryVectorIndex _index = new();
    private readonly IndexingQueue _queue = new();
    private readonly ReindexProgress _progress = new();

    public SemanticSearchIntegrationTests(WebApplicationFactory<Program> factory)
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
            services.AddSingleton<IEmbeddingService>(_embeddings);
            services.AddSingleton<IVectorIndex>(_index);
            services.AddSingleton(_queue);
            services.AddSingleton(_progress);
            services.AddScoped<SemanticIndexer>();
        }));
        _client = _factory.CreateClient();
    }

    private async Task<Guid> SeedIndexedJobAsync(string owner, params string[] segments)
    {
        var job = new AudioJob { OwnerId = owner, FileName = $"{Guid.NewGuid():N}.mp3", ContentType = "audio/mpeg", Model = "Base", Status = AudioJobStatus.Completed };
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AudioJobs.Add(job);
        for (var i = 0; i < segments.Length; i++)
            db.TranscriptSegments.Add(new TranscriptSegment { AudioJobId = job.Id, Index = i, StartMs = i * 60_000L, EndMs = i * 60_000L + 50_000, Text = segments[i] });
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<SemanticIndexer>().IndexJobAsync(job.Id);
        return job.Id;
    }

    private Task<HttpResponseMessage> GetAsync(string url, string? user = null, string? roles = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (user is not null) request.Headers.Add(TestAuthHandler.UserIdHeader, user);
        if (roles is not null) request.Headers.Add(TestAuthHandler.RolesHeader, roles);
        return _client.SendAsync(request);
    }

    // --- search by meaning -------------------------------------------------------------

    [Fact]
    public async Task Search_FindsTheSpotByMeaning_NotByWord()
    {
        var job = await SeedIndexedJobAsync(TestAuthHandler.DefaultUserId, "Zuerst begrüßen wir alle.", "In der Budgetrunde geht es um die Kosten.", "Danach gibt es Pizza.");

        var result = await _client.GetFromJsonAsync<PaginatedResult<SearchHitDto>>("/api/search/semantic?q=Wann ging es ums Geld?");

        result!.Items.Should().NotBeEmpty();
        var best = result.Items[0];
        best.JobId.Should().Be(job);
        best.Snippet.Should().Contain("Budgetrunde");
        best.SegmentStartMs.Should().NotBeNull();
        best.HighlightLength.Should().Be(0);
    }

    [Fact]
    public async Task Search_NeverShowsHitsOfAnotherUsersJob()
    {
        await SeedIndexedJobAsync(Other, "Die Budgetrunde war lang.");
        var mine = await SeedIndexedJobAsync(TestAuthHandler.DefaultUserId, "Mein Budget ist klein.");

        var result = await _client.GetFromJsonAsync<PaginatedResult<SearchHitDto>>("/api/search/semantic?q=Geld");

        result!.Items.Select(i => i.JobId).Should().OnlyContain(id => id == mine);
        var theirs = await GetAsync("/api/search/semantic?q=Geld", user: Other);
        (await theirs.Content.ReadFromJsonAsync<PaginatedResult<SearchHitDto>>())!.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Search_IncludesJobsSharedWithTheUser()
    {
        var shared = await SeedIndexedJobAsync(Other, "Die Kosten steigen.");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.JobShares.Add(new JobShare { AudioJobId = shared, UserId = TestAuthHandler.DefaultUserId });
            await db.SaveChangesAsync();
        }

        var result = await _client.GetFromJsonAsync<PaginatedResult<SearchHitDto>>("/api/search/semantic?q=Geld");

        result!.Items.Should().ContainSingle().Which.JobId.Should().Be(shared);
    }

    [Fact]
    public async Task Search_AfterTheShareIsRemoved_NoLongerFindsTheJob()
    {
        var shared = await SeedIndexedJobAsync(Other, "Die Kosten steigen.");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var share = new JobShare { AudioJobId = shared, UserId = TestAuthHandler.DefaultUserId };
            db.JobShares.Add(share);
            await db.SaveChangesAsync();
            db.JobShares.Remove(share);
            await db.SaveChangesAsync();
        }

        var result = await _client.GetFromJsonAsync<PaginatedResult<SearchHitDto>>("/api/search/semantic?q=Geld");

        result!.Items.Should().BeEmpty("the permission is checked at search time, not when the index was built");
    }

    [Fact]
    public async Task Search_PagesTheResults()
    {
        await SeedIndexedJobAsync(TestAuthHandler.DefaultUserId, Enumerable.Range(0, 5).Select(i => $"Budget Kosten Nummer {i}.").ToArray());
        // The chunker merges segments of one minute; seed more jobs so that there are several hits
        for (var i = 0; i < 4; i++)
            await SeedIndexedJobAsync(TestAuthHandler.DefaultUserId, "Das Budget und die Kosten.");

        var first = await _client.GetFromJsonAsync<PaginatedResult<SearchHitDto>>("/api/search/semantic?q=Geld&pageSize=2&page=1");
        var second = await _client.GetFromJsonAsync<PaginatedResult<SearchHitDto>>("/api/search/semantic?q=Geld&pageSize=2&page=2");

        first!.Items.Should().HaveCount(2);
        second!.Items.Should().NotBeEmpty();
        first.TotalCount.Should().BeGreaterThan(2);
    }

    [Fact]
    public async Task Search_WithoutQuery_Returns400()
    {
        (await GetAsync("/api/search/semantic")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetAsync("/api/search/semantic?q=Geld&mode=quatsch")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Search_BeforeAnythingIsIndexed_ReturnsNoHits()
    {
        var result = await _client.GetFromJsonAsync<PaginatedResult<SearchHitDto>>("/api/search/semantic?q=Geld");

        result!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_AfterAModelChange_Returns409UntilTheIndexIsRebuilt()
    {
        await SeedIndexedJobAsync(TestAuthHandler.DefaultUserId, "Budget");
        _embeddings.Info = new EmbeddingModelInfo("Fake", "neues-modell", FakeEmbeddingService.Concepts.Length);

        (await _client.GetAsync("/api/search/semantic?q=Geld")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // --- modes -----------------------------------------------------------------------

    [Fact]
    public async Task Modes_SayWhetherSemanticSearchCanBeUsed()
    {
        var before = await _client.GetFromJsonAsync<SearchModesDto>("/api/search/modes");
        await SeedIndexedJobAsync(TestAuthHandler.DefaultUserId, "Budget");
        var after = await _client.GetFromJsonAsync<SearchModesDto>("/api/search/modes");
        _embeddings.Info = new EmbeddingModelInfo("Fake", "neues-modell", FakeEmbeddingService.Concepts.Length);
        var stale = await _client.GetFromJsonAsync<SearchModesDto>("/api/search/modes");

        before.Should().Be(new SearchModesDto(true, "NotBuilt"));
        after.Should().Be(new SearchModesDto(true, "Ready"));
        stale.Should().Be(new SearchModesDto(false, "Stale"));
    }

    // --- admin ---------------------------------------------------------------------------

    [Fact]
    public async Task Reindex_IsForAdminsOnly()
    {
        (await _client.PostAsync("/api/admin/search/reindex", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client.GetAsync("/api/admin/search/status")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reindex_QueuesTheRebuild_AndRefusesASecondOneWhileItRuns()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/search/reindex");
        request.Headers.Add(TestAuthHandler.RolesHeader, Roles.Admin);
        var again = new HttpRequestMessage(HttpMethod.Post, "/api/admin/search/reindex");
        again.Headers.Add(TestAuthHandler.RolesHeader, Roles.Admin);

        var first = await _client.SendAsync(request);
        var second = await _client.SendAsync(again);

        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _progress.Running.Should().BeTrue();
    }

    [Fact]
    public async Task Worker_RebuildsAfterAModelChange_AndTheStatusFollows()
    {
        await SeedIndexedJobAsync(TestAuthHandler.DefaultUserId, "Budget Kosten");
        _embeddings.Info = new EmbeddingModelInfo("Fake", "neues-modell", FakeEmbeddingService.Concepts.Length);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/search/reindex");
        request.Headers.Add(TestAuthHandler.RolesHeader, Roles.Admin);
        await _client.SendAsync(request);

        var worker = new IndexingWorker(_queue, _progress, _factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<IndexingWorker>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var queued in _queue.ReadAllAsync(cts.Token))
        {
            await worker.ProcessAsync(queued, CancellationToken.None);
            break;
        }

        var status = new HttpRequestMessage(HttpMethod.Get, "/api/admin/search/status");
        status.Headers.Add(TestAuthHandler.RolesHeader, Roles.Admin);
        var dto = await (await _client.SendAsync(status)).Content.ReadFromJsonAsync<SemanticIndexStatusDto>();
        dto!.Status.Should().Be("Ready");
        dto.Running.Should().BeFalse();
        dto.Model.Should().Be("neues-modell");
        (dto.Done, dto.Total).Should().Be((1, 1));
        (await _client.GetAsync("/api/search/semantic?q=Geld")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // --- keeping the index in step -----------------------------------------------------------

    [Fact]
    public async Task EditingASegment_QueuesTheJobForIndexing()
    {
        var jobId = await SeedIndexedJobAsync(TestAuthHandler.DefaultUserId, "Wetter heute.");

        var response = await _client.PutAsJsonAsync($"/api/audio-jobs/{jobId}/segments/0", new EditSegmentRequest("Die Finanzen heute.", 0));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var queued = await _queue.ReadAllAsync(cts.Token).FirstAsync(cts.Token);
        queued.JobId.Should().Be(jobId);
    }

    [Fact]
    public async Task DeletingAJob_RemovesItsVectors()
    {
        var jobId = await SeedIndexedJobAsync(TestAuthHandler.DefaultUserId, "Budget");
        _index.Entries.Should().NotBeEmpty();

        (await _client.DeleteAsync($"/api/audio-jobs/{jobId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        _index.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task IndexingTheSameJobTwiceInTheQueue_IsQueuedOnce()
    {
        var id = Guid.NewGuid();
        _queue.Enqueue(id);
        _queue.Enqueue(id);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var seen = new List<IndexingRequest>();
        try
        {
            await foreach (var request in _queue.ReadAllAsync(cts.Token))
                seen.Add(request);
        }
        catch (OperationCanceledException)
        {
            // the queue is open-ended: the timeout ends the read
        }

        seen.Should().ContainSingle();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
