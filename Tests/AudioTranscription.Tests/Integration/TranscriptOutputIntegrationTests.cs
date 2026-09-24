using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AudioTranscription.Api.Configuration;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Api.Storage;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Domain.Subtitles;
using AudioTranscription.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AudioTranscription.Tests.Integration;

/// <summary>S06: segments, subtitle download and audio streaming of a job.</summary>
public class TranscriptOutputIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly byte[] AudioBytes = [0x49, 0x44, 0x33, 1, 2, 3, 4, 5, 6, 7];

    private readonly TempDirectory _dir = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public TranscriptOutputIntegrationTests(WebApplicationFactory<Program> factory)
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
            services.Configure<UploadOptions>(o => o.TempStoragePath = _dir.Path);
        }));
        _client = _factory.CreateClient();
    }

    // --- segments -------------------------------------------------------------

    [Fact]
    public async Task Segments_OfCompletedJob_AreReturnedInOrder()
    {
        var job = await SeedAsync(AudioJobStatus.Completed);

        var segments = await _client.GetFromJsonAsync<List<TranscriptSegmentDto>>($"/api/audio-jobs/{job.Id}/segments");

        segments.Should().Equal(
            new TranscriptSegmentDto(0, 0, 1_500, "Hallo zusammen."),
            new TranscriptSegmentDto(1, 1_500, 3_000, "Los geht's."));
    }

    [Theory]
    [InlineData("segments")]
    [InlineData("subtitles?format=srt")]
    public async Task Output_OfUnfinishedJob_Returns409(string action)
    {
        var job = await SeedAsync(AudioJobStatus.Processing);

        var response = await _client.GetAsync($"/api/audio-jobs/{job.Id}/{action}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("segments")]
    [InlineData("subtitles?format=srt")]
    [InlineData("audio")]
    public async Task Output_OfUnknownJob_Returns404(string action)
    {
        var response = await _client.GetAsync($"/api/audio-jobs/{Guid.NewGuid()}/{action}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- subtitles ------------------------------------------------------------

    [Theory]
    [InlineData("srt", "application/x-subrip")]
    [InlineData("vtt", "text/vtt")]
    [InlineData("VTT", "text/vtt")]
    public async Task Subtitles_AreDownloadedAsFile(string format, string contentType)
    {
        var job = await SeedAsync(AudioJobStatus.Completed, fileName: "Besprechung Müller.mp3");

        var response = await _client.GetAsync($"/api/audio-jobs/{job.Id}/subtitles?format={format}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(contentType);
        response.Content.Headers.ContentType.CharSet.Should().Be("utf-8");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        response.Content.Headers.ContentDisposition.FileNameStar.Should().Be($"Besprechung Müller.{format.ToLowerInvariant()}");
        var expected = format.ToLowerInvariant() == "srt"
            ? SubtitleFormatter.ToSrt(await SegmentsAsync(job.Id))
            : SubtitleFormatter.ToVtt(await SegmentsAsync(job.Id));
        (await response.Content.ReadAsStringAsync()).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?format=")]
    [InlineData("?format=txt")]
    public async Task Subtitles_WithUnknownFormat_Return400(string query)
    {
        var job = await SeedAsync(AudioJobStatus.Completed);

        var response = await _client.GetAsync($"/api/audio-jobs/{job.Id}/subtitles{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Subtitles_FileName_IsSanitized()
    {
        var job = await SeedAsync(AudioJobStatus.Completed, fileName: "..\\evil\"na;me\r\n.mp3");

        var response = await _client.GetAsync($"/api/audio-jobs/{job.Id}/subtitles?format=srt");

        var fileName = response.Content.Headers.ContentDisposition!.FileNameStar;
        fileName.Should().MatchRegex(@"^[\w .-]+\.srt$").And.StartWith("evil");
    }

    [Theory]
    [InlineData("")]
    [InlineData(".mp3")]
    [InlineData("\"\"\".mp3")]
    public async Task Subtitles_FileName_FallsBackWhenNothingIsLeft(string originalName)
    {
        var job = await SeedAsync(AudioJobStatus.Completed, fileName: originalName);

        var response = await _client.GetAsync($"/api/audio-jobs/{job.Id}/subtitles?format=vtt");

        response.Content.Headers.ContentDisposition!.FileNameStar.Should().Be("transcript.vtt");
    }

    // --- audio ----------------------------------------------------------------

    [Fact]
    public async Task Audio_WithRangeHeader_Returns206WithThatRange()
    {
        var job = await SeedAsync(AudioJobStatus.Completed);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/audio-jobs/{job.Id}/audio");
        request.Headers.Range = new RangeHeaderValue(2, 5);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        response.Content.Headers.ContentRange!.ToString().Should().Be("bytes 2-5/10");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(AudioBytes[2..6]);
    }

    [Fact]
    public async Task Audio_WithoutRange_ReturnsWholeFileWithContentType()
    {
        var job = await SeedAsync(AudioJobStatus.Completed);

        var response = await _client.GetAsync($"/api/audio-jobs/{job.Id}/audio");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("audio/mpeg");
        response.Headers.AcceptRanges.Should().Contain("bytes");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(AudioBytes);
    }

    [Fact]
    public async Task Audio_WhenUploadWasRemoved_Returns410()
    {
        var job = await SeedAsync(AudioJobStatus.Completed, withUpload: false);

        var response = await _client.GetAsync($"/api/audio-jobs/{job.Id}/audio");

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
    }

    // --- helpers --------------------------------------------------------------

    private async Task<AudioJob> SeedAsync(AudioJobStatus status, string fileName = "meeting.mp3", bool withUpload = true)
    {
        var job = new AudioJob
        {
            FileName = fileName,
            ContentType = "audio/mpeg",
            Model = "Base",
            Status = status,
            RawTranscript = status == AudioJobStatus.Completed ? "Hallo zusammen. Los geht's." : null,
        };
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AudioJobs.Add(job);
        if (status == AudioJobStatus.Completed)
        {
            // Stored out of order on purpose
            db.TranscriptSegments.AddRange(
                new TranscriptSegment { AudioJobId = job.Id, Index = 1, StartMs = 1_500, EndMs = 3_000, Text = "Los geht's." },
                new TranscriptSegment { AudioJobId = job.Id, Index = 0, StartMs = 0, EndMs = 1_500, Text = "Hallo zusammen." });
        }
        await db.SaveChangesAsync();

        if (withUpload)
        {
            var store = _factory.Services.GetRequiredService<ITempFileStore>();
            Directory.CreateDirectory(store.StorageDirectory);
            await File.WriteAllBytesAsync(store.GetUploadPath(job.Id, job.FileName), AudioBytes);
        }
        return job;
    }

    private async Task<List<TranscriptSegment>> SegmentsAsync(Guid jobId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().TranscriptSegments
            .Where(s => s.AudioJobId == jobId).ToListAsync();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        _dir.Dispose();
    }
}
