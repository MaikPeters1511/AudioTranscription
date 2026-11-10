using System.Net;
using System.Net.Http.Json;
using AudioTranscription.Api.BackgroundServices;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Domain.Chapters;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using AudioTranscription.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AudioTranscription.Tests.Integration;

/// <summary>S25: generating, reading, editing and deleting chapters.</summary>
public class ChapterIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Mock<IChapterGenerator> _generator = new();
    private readonly ChapterQueue _queue = new();

    public ChapterIntegrationTests(WebApplicationFactory<Program> factory)
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
            services.AddSingleton(_generator.Object);
            services.AddSingleton(_queue);
        }));
        _client = _factory.CreateClient();
    }

    // --- POST ------------------------------------------------------------------

    [Fact]
    public async Task Generate_ForACompletedJob_Returns202_AndQueuesAPendingRun()
    {
        var job = await SeedAsync();

        var response = await _client.PostAsync($"/api/audio-jobs/{job.Id}/chapters", null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await response.Content.ReadFromJsonAsync<ChaptersDto>())!.Status.Should().Be("Pending");
        using var scope = _factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().ChapterRuns.SingleAsync()).Status.Should().Be(VariantStatus.Pending);
    }

    [Fact]
    public async Task Generate_WhileARunIsPending_DoesNotStartASecondOne()
    {
        var job = await SeedAsync();
        await _client.PostAsync($"/api/audio-jobs/{job.Id}/chapters", null);

        var again = await _client.PostAsync($"/api/audio-jobs/{job.Id}/chapters", null);

        again.StatusCode.Should().Be(HttpStatusCode.Accepted);
        using var scope = _factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().ChapterRuns.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Generate_ForAnUnfinishedJob_Returns409()
    {
        var job = await SeedAsync(AudioJobStatus.Processing);

        (await _client.PostAsync($"/api/audio-jobs/{job.Id}/chapters", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Generate_ForAJobWithoutSegments_Returns409()
    {
        var job = await SeedAsync(segments: 0);

        (await _client.PostAsync($"/api/audio-jobs/{job.Id}/chapters", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Generate_ForAnUnknownJob_Returns404() =>
        (await _client.PostAsync($"/api/audio-jobs/{Guid.NewGuid()}/chapters", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

    [Fact]
    public async Task Generate_ForSomeoneElsesJob_Returns404()
    {
        var job = await SeedAsync(ownerId: "someone-else");

        (await _client.PostAsync($"/api/audio-jobs/{job.Id}/chapters", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- GET -------------------------------------------------------------------

    [Fact]
    public async Task Get_BeforeAnyRun_SaysNone()
    {
        var job = await SeedAsync();

        var chapters = await _client.GetFromJsonAsync<ChaptersDto>($"/api/audio-jobs/{job.Id}/chapters");

        chapters!.Status.Should().Be("None");
        chapters.Chapters.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_ReturnsTheChaptersInOrder()
    {
        var job = await SeedAsync();
        await SeedChaptersAsync(job.Id, ("Zwei", 10_000, 20_000), ("Eins", 0, 10_000));

        var chapters = await _client.GetFromJsonAsync<ChaptersDto>($"/api/audio-jobs/{job.Id}/chapters");

        chapters!.Chapters.Select(c => c.Title).Should().Equal("Zwei", "Eins"); // by Position, as stored
    }

    // --- worker ----------------------------------------------------------------

    [Fact]
    public async Task Worker_StoresTheChapters_OnSegmentBoundaries()
    {
        var job = await SeedAsync(segments: 6);
        _generator.Setup(g => g.GenerateAsync(It.IsAny<IReadOnlyList<ChapterSegment>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ChapterDraft(0, "Anfang", "a"), new ChapterDraft(3, "Ende", "b")]);
        await _client.PostAsync($"/api/audio-jobs/{job.Id}/chapters", null);

        await RunWorkerAsync();

        var chapters = await _client.GetFromJsonAsync<ChaptersDto>($"/api/audio-jobs/{job.Id}/chapters");
        chapters!.Status.Should().Be("Completed");
        chapters.Chapters.Select(c => (c.Title, c.StartMs, c.EndMs)).Should().Equal(("Anfang", 0L, 15_000L), ("Ende", 15_000L, 30_000L));
    }

    [Fact]
    public async Task Worker_UsesTheCorrectedSegmentText()
    {
        var job = await SeedAsync(segments: 2);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.TranscriptSegments.SingleAsync(s => s.AudioJobId == job.Id && s.Index == 0)).CurrentText = "Korrigierter Text";
            await db.SaveChangesAsync();
        }
        IReadOnlyList<ChapterSegment>? seen = null;
        _generator.Setup(g => g.GenerateAsync(It.IsAny<IReadOnlyList<ChapterSegment>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<ChapterSegment>, CancellationToken>((s, _) => seen = s)
            .ReturnsAsync([new ChapterDraft(0, "A", "")]);
        await _client.PostAsync($"/api/audio-jobs/{job.Id}/chapters", null);

        await RunWorkerAsync();

        seen![0].Text.Should().Be("Korrigierter Text");
    }

    [Fact]
    public async Task Worker_WithAnUnusableAnswer_MarksTheRunFailed_AndKeepsOldChapters()
    {
        var job = await SeedAsync();
        await SeedChaptersAsync(job.Id, ("Alt", 0, 30_000));
        _generator.Setup(g => g.GenerateAsync(It.IsAny<IReadOnlyList<ChapterSegment>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ChapterParseException("kein JSON"));
        await _client.PostAsync($"/api/audio-jobs/{job.Id}/chapters", null);

        await RunWorkerAsync();

        var chapters = await _client.GetFromJsonAsync<ChaptersDto>($"/api/audio-jobs/{job.Id}/chapters");
        chapters!.Status.Should().Be("Failed");
        chapters.ErrorMessage.Should().Be(ChapterWorker.ParseFailureMessage);
        chapters.Chapters.Select(c => c.Title).Should().Equal("Alt");
    }

    [Fact]
    public async Task Worker_WhenTheModelIsUnreachable_MarksTheRunFailed_WithoutLeakingDetails()
    {
        var job = await SeedAsync();
        _generator.Setup(g => g.GenerateAsync(It.IsAny<IReadOnlyList<ChapterSegment>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connect ECONNREFUSED 10.0.0.5:11434"));
        await _client.PostAsync($"/api/audio-jobs/{job.Id}/chapters", null);

        await RunWorkerAsync();

        var chapters = await _client.GetFromJsonAsync<ChaptersDto>($"/api/audio-jobs/{job.Id}/chapters");
        chapters!.Status.Should().Be("Failed");
        chapters.ErrorMessage.Should().Be(ChapterWorker.GenerationFailureMessage).And.NotContain("10.0.0.5");
    }

    [Fact]
    public async Task Worker_Regeneration_ReplacesTheOldChapters()
    {
        var job = await SeedAsync(segments: 4);
        await SeedChaptersAsync(job.Id, ("Alt", 0, 20_000));
        _generator.Setup(g => g.GenerateAsync(It.IsAny<IReadOnlyList<ChapterSegment>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ChapterDraft(0, "Neu", "")]);
        await _client.PostAsync($"/api/audio-jobs/{job.Id}/chapters", null);

        await RunWorkerAsync();

        var chapters = await _client.GetFromJsonAsync<ChaptersDto>($"/api/audio-jobs/{job.Id}/chapters");
        chapters!.Chapters.Select(c => c.Title).Should().Equal("Neu");
    }

    // --- PUT / DELETE ----------------------------------------------------------

    [Fact]
    public async Task Update_ChangesTitleAndSummary_ButNotTheLimits()
    {
        var job = await SeedAsync();
        var ids = await SeedChaptersAsync(job.Id, ("Alt", 0, 30_000));

        var response = await _client.PutAsJsonAsync($"/api/audio-jobs/{job.Id}/chapters/{ids[0]}", new UpdateChapterRequest("  Neu  ", "Text"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var chapter = await response.Content.ReadFromJsonAsync<ChapterDto>();
        (chapter!.Title, chapter.Summary, chapter.StartMs, chapter.EndMs).Should().Be(("Neu", "Text", 0L, 30_000L));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Update_WithoutATitle_Returns400(string title)
    {
        var job = await SeedAsync();
        var ids = await SeedChaptersAsync(job.Id, ("Alt", 0, 30_000));

        var response = await _client.PutAsJsonAsync($"/api/audio-jobs/{job.Id}/chapters/{ids[0]}", new UpdateChapterRequest(title, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_OfAChapterOfAnotherJob_Returns404()
    {
        var job = await SeedAsync();
        var other = await SeedAsync();
        var ids = await SeedChaptersAsync(other.Id, ("Fremd", 0, 30_000));

        var response = await _client.PutAsJsonAsync($"/api/audio-jobs/{job.Id}/chapters/{ids[0]}", new UpdateChapterRequest("x", null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_GivesTheTimeRangeToThePreviousChapter_AndRenumbers()
    {
        var job = await SeedAsync();
        var ids = await SeedChaptersAsync(job.Id, ("A", 0, 10_000), ("B", 10_000, 20_000), ("C", 20_000, 30_000));

        (await _client.DeleteAsync($"/api/audio-jobs/{job.Id}/chapters/{ids[1]}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var chapters = (await _client.GetFromJsonAsync<ChaptersDto>($"/api/audio-jobs/{job.Id}/chapters"))!.Chapters;
        chapters.Select(c => (c.Position, c.Title, c.StartMs, c.EndMs)).Should().Equal((0, "A", 0L, 20_000L), (1, "C", 20_000L, 30_000L));
    }

    [Fact]
    public async Task Delete_OfTheFirstChapter_GivesItsRangeToTheNext()
    {
        var job = await SeedAsync();
        var ids = await SeedChaptersAsync(job.Id, ("A", 0, 10_000), ("B", 10_000, 30_000));

        await _client.DeleteAsync($"/api/audio-jobs/{job.Id}/chapters/{ids[0]}");

        var chapters = (await _client.GetFromJsonAsync<ChaptersDto>($"/api/audio-jobs/{job.Id}/chapters"))!.Chapters;
        chapters.Should().ContainSingle().Which.Should().Match<ChapterDto>(c => c.StartMs == 0 && c.EndMs == 30_000 && c.Position == 0);
    }

    [Fact]
    public async Task Delete_OfAnUnknownChapter_Returns404()
    {
        var job = await SeedAsync();

        (await _client.DeleteAsync($"/api/audio-jobs/{job.Id}/chapters/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- helpers ---------------------------------------------------------------

    private async Task<AudioJob> SeedAsync(AudioJobStatus status = AudioJobStatus.Completed, int segments = 6, string? ownerId = null)
    {
        var job = new AudioJob
        {
            OwnerId = ownerId ?? TestAuthHandler.DefaultUserId,
            FileName = "meeting.mp3", ContentType = "audio/mpeg", Model = "Base",
            Status = status, RawTranscript = "Hallo",
        };
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AudioJobs.Add(job);
        for (var i = 0; i < segments; i++)
            db.TranscriptSegments.Add(new TranscriptSegment { AudioJobId = job.Id, Index = i, StartMs = i * 5_000L, EndMs = (i + 1) * 5_000L, Text = $"Satz {i}" });
        await db.SaveChangesAsync();
        return job;
    }

    private async Task<List<Guid>> SeedChaptersAsync(Guid jobId, params (string Title, long StartMs, long EndMs)[] chapters)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var created = chapters.Select((c, i) => new TranscriptChapter { AudioJobId = jobId, Position = i, Title = c.Title, StartMs = c.StartMs, EndMs = c.EndMs }).ToList();
        db.TranscriptChapters.AddRange(created);
        await db.SaveChangesAsync();
        return created.Select(c => c.Id).ToList();
    }

    /// <summary>Runs the queued request the way the background service would.</summary>
    private async Task RunWorkerAsync()
    {
        var worker = new ChapterWorker(_queue, _factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<ChapterWorker>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var request in _queue.ReadAllAsync(cts.Token))
        {
            await worker.ProcessAsync(request, CancellationToken.None);
            break;
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
