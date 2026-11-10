using System.Net;
using System.Net.Http.Json;
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

/// <summary>S32: correcting segments with versioning; the raw transcript is never overwritten.</summary>
public class SegmentEditIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public SegmentEditIntegrationTests(WebApplicationFactory<Program> factory)
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
        }));
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Edit_StoresNewVersion_AndSegmentsReturnCorrectedText()
    {
        var job = await SeedAsync();

        var response = await PutAsync(job.Id, 0, "Hallo zusammen!", expectedVersion: 0);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var edited = await response.Content.ReadFromJsonAsync<TranscriptSegmentDto>();
        edited!.Text.Should().Be("Hallo zusammen!");
        edited.Version.Should().Be(1);
        var segments = await _client.GetFromJsonAsync<List<TranscriptSegmentDto>>($"/api/audio-jobs/{job.Id}/segments");
        segments!.Select(s => s.Text).Should().Equal("Hallo zusammen!", "Los geht's.");
        segments[0].Version.Should().Be(1);
    }

    [Fact]
    public async Task Edit_NeverOverwritesTheOriginal()
    {
        var job = await SeedAsync();

        await PutAsync(job.Id, 0, "Korrigiert", expectedVersion: 0);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.TranscriptSegments.SingleAsync(s => s.AudioJobId == job.Id && s.Index == 0)).Text.Should().Be("Hallo zusammen.");
        (await db.AudioJobs.FindAsync(job.Id))!.RawTranscript.Should().Be("Hallo zusammen. Los geht's.");
    }

    [Fact]
    public async Task Edit_WithStaleVersion_Returns409_AndKeepsTheOtherChange()
    {
        var job = await SeedAsync();
        await PutAsync(job.Id, 0, "Erste Korrektur", expectedVersion: 0);

        var response = await PutAsync(job.Id, 0, "Gleichzeitige Korrektur", expectedVersion: 0);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var segments = await _client.GetFromJsonAsync<List<TranscriptSegmentDto>>($"/api/audio-jobs/{job.Id}/segments");
        segments![0].Text.Should().Be("Erste Korrektur");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Edit_WithEmptyText_Returns400(string text)
    {
        var job = await SeedAsync();

        var response = await PutAsync(job.Id, 0, text, expectedVersion: 0);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Edit_OfUnknownSegment_Returns404()
    {
        var job = await SeedAsync();

        var response = await PutAsync(job.Id, 99, "x", expectedVersion: 0);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Edit_OfUnfinishedJob_Returns409()
    {
        var job = await SeedAsync(AudioJobStatus.Processing);

        var response = await PutAsync(job.Id, 0, "x", expectedVersion: 0);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task History_ListsOriginalAndAllVersions_NewestFirst()
    {
        var job = await SeedAsync();
        await PutAsync(job.Id, 0, "Eins", expectedVersion: 0);
        await PutAsync(job.Id, 0, "Zwei", expectedVersion: 1);

        var history = await _client.GetFromJsonAsync<List<SegmentVersionDto>>($"/api/audio-jobs/{job.Id}/segments/0/history");

        history!.Select(h => (h.Version, h.Text)).Should().Equal((2, "Zwei"), (1, "Eins"), (0, "Hallo zusammen."));
        history[0].EditedBy.Should().Be(TestAuthHandler.DefaultUserId);
        history[2].EditedBy.Should().BeNull();
    }

    [Fact]
    public async Task Restore_CreatesANewVersion_WithTheOldText()
    {
        var job = await SeedAsync();
        await PutAsync(job.Id, 0, "Eins", expectedVersion: 0);
        await PutAsync(job.Id, 0, "Zwei", expectedVersion: 1);

        var response = await _client.PostAsJsonAsync($"/api/audio-jobs/{job.Id}/segments/0/restore",
            new RestoreSegmentRequest(Version: 1, ExpectedVersion: 2));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var restored = await response.Content.ReadFromJsonAsync<TranscriptSegmentDto>();
        restored!.Text.Should().Be("Eins");
        restored.Version.Should().Be(3);
        var history = await _client.GetFromJsonAsync<List<SegmentVersionDto>>($"/api/audio-jobs/{job.Id}/segments/0/history");
        history!.Should().HaveCount(4);
    }

    [Fact]
    public async Task Restore_OfOriginal_ReturnsTheRawText()
    {
        var job = await SeedAsync();
        await PutAsync(job.Id, 0, "Eins", expectedVersion: 0);

        var response = await _client.PostAsJsonAsync($"/api/audio-jobs/{job.Id}/segments/0/restore",
            new RestoreSegmentRequest(Version: 0, ExpectedVersion: 1));

        (await response.Content.ReadFromJsonAsync<TranscriptSegmentDto>())!.Text.Should().Be("Hallo zusammen.");
    }

    [Fact]
    public async Task Restore_WithUnknownVersion_Returns404_AndStaleVersion409()
    {
        var job = await SeedAsync();
        await PutAsync(job.Id, 0, "Eins", expectedVersion: 0);

        var unknown = await _client.PostAsJsonAsync($"/api/audio-jobs/{job.Id}/segments/0/restore",
            new RestoreSegmentRequest(Version: 7, ExpectedVersion: 1));
        var stale = await _client.PostAsJsonAsync($"/api/audio-jobs/{job.Id}/segments/0/restore",
            new RestoreSegmentRequest(Version: 0, ExpectedVersion: 0));

        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Subtitles_AndExport_ContainTheCorrectedText()
    {
        var job = await SeedAsync();
        await PutAsync(job.Id, 0, "Hallo Welt", expectedVersion: 0);

        var srt = await _client.GetStringAsync($"/api/audio-jobs/{job.Id}/subtitles?format=srt");
        var export = await _client.GetStringAsync($"/api/audio-jobs/{job.Id}/export?format=md");

        srt.Should().Contain("Hallo Welt").And.NotContain("Hallo zusammen.");
        export.Should().Contain("Hallo Welt").And.NotContain("Hallo zusammen.");
    }

    [Fact]
    public async Task Edit_BySomeoneElse_IsRejected()
    {
        var job = await SeedAsync();
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/audio-jobs/{job.Id}/segments/0")
        {
            Content = JsonContent.Create(new EditSegmentRequest("Fremd", 0))
        };
        request.Headers.Add(TestAuthHandler.UserIdHeader, "someone-else");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- helpers --------------------------------------------------------------

    private Task<HttpResponseMessage> PutAsync(Guid jobId, int index, string text, int expectedVersion) =>
        _client.PutAsJsonAsync($"/api/audio-jobs/{jobId}/segments/{index}", new EditSegmentRequest(text, expectedVersion));

    private async Task<AudioJob> SeedAsync(AudioJobStatus status = AudioJobStatus.Completed)
    {
        var job = new AudioJob
        {
            OwnerId = TestAuthHandler.DefaultUserId,
            FileName = "meeting.mp3",
            ContentType = "audio/mpeg",
            Model = "Base",
            Status = status,
            RawTranscript = "Hallo zusammen. Los geht's.",
        };
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AudioJobs.Add(job);
        db.TranscriptSegments.AddRange(
            new TranscriptSegment { AudioJobId = job.Id, Index = 0, StartMs = 0, EndMs = 1_500, Text = "Hallo zusammen." },
            new TranscriptSegment { AudioJobId = job.Id, Index = 1, StartMs = 1_500, EndMs = 3_000, Text = "Los geht's." });
        await db.SaveChangesAsync();
        return job;
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
