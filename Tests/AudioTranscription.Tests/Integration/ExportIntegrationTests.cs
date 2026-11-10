using System.Net;
using System.Text;
using System.Text.Json;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AudioTranscription.Tests.Integration;

/// <summary>S21: GET /api/audio-jobs/{id}/export in DOCX, Markdown, JSON and PDF.</summary>
public class ExportIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ExportIntegrationTests(WebApplicationFactory<Program> factory)
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

    [Theory]
    [InlineData("md", "text/markdown")]
    [InlineData("json", "application/json")]
    [InlineData("docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("pdf", "application/pdf")]
    [InlineData("PDF", "application/pdf")]
    public async Task Export_IsDownloadedAsFile(string format, string contentType)
    {
        var job = await SeedAsync(AudioJobStatus.Completed, fileName: "Besprechung Müller.mp3");

        var response = await _client.GetAsync($"/api/audio-jobs/{job.Id}/export?format={format}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(contentType);
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        response.Content.Headers.ContentDisposition.FileNameStar.Should().Be($"Besprechung Müller.{format.ToLowerInvariant()}");
        (await response.Content.ReadAsByteArrayAsync()).Should().NotBeEmpty();
    }

    [Fact]
    public async Task Export_Markdown_ContainsSpeakersAndTimestampsByDefault()
    {
        var job = await SeedAsync(AudioJobStatus.Completed);

        var markdown = await GetTextAsync($"/api/audio-jobs/{job.Id}/export?format=md");

        markdown.Should().Contain("**Anna** `00:00:00`").And.Contain("Hallo zusammen. Los geht's.");
    }

    [Fact]
    public async Task Export_Markdown_RespectsOptions()
    {
        var job = await SeedAsync(AudioJobStatus.Completed);

        var markdown = await GetTextAsync($"/api/audio-jobs/{job.Id}/export?format=md&timestamps=false&speakers=false");

        markdown.Should().NotContain("`00:").And.NotContain("Anna").And.Contain("Hallo zusammen.");
    }

    [Fact]
    public async Task Export_Json_HasSchemaVersion()
    {
        var job = await SeedAsync(AudioJobStatus.Completed);

        using var json = JsonDocument.Parse(await GetTextAsync($"/api/audio-jobs/{job.Id}/export?format=json"));

        json.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(1);
        json.RootElement.GetProperty("blocks").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Export_OfVariant_ExportsTheVariantText()
    {
        var job = await SeedAsync(AudioJobStatus.Completed);
        var variant = await SeedVariantAsync(job.Id, PostProcessingMode.Summary, VariantStatus.Completed, "Kurz und knapp.");

        var response = await _client.GetAsync($"/api/audio-jobs/{job.Id}/export?format=md&variant={variant.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var markdown = await response.Content.ReadAsStringAsync();
        markdown.Should().Contain("Kurz und knapp.").And.Contain("Summary").And.NotContain("Hallo zusammen.");
        response.Content.Headers.ContentDisposition!.FileNameStar.Should().Be("meeting-summary.md");
    }

    [Fact]
    public async Task Export_OfUnfinishedVariant_Returns409()
    {
        var job = await SeedAsync(AudioJobStatus.Completed);
        var variant = await SeedVariantAsync(job.Id, PostProcessingMode.Summary, VariantStatus.Pending, null);

        var response = await _client.GetAsync($"/api/audio-jobs/{job.Id}/export?format=md&variant={variant.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Export_OfVariantFromAnotherJob_Returns404()
    {
        var job = await SeedAsync(AudioJobStatus.Completed);
        var other = await SeedAsync(AudioJobStatus.Completed);
        var variant = await SeedVariantAsync(other.Id, PostProcessingMode.Summary, VariantStatus.Completed, "Fremd.");

        var response = await _client.GetAsync($"/api/audio-jobs/{job.Id}/export?format=md&variant={variant.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?format=")]
    [InlineData("?format=txt")]
    public async Task Export_WithUnknownFormat_Returns400(string query)
    {
        var job = await SeedAsync(AudioJobStatus.Completed);

        var response = await _client.GetAsync($"/api/audio-jobs/{job.Id}/export{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Export_OfUnfinishedJob_Returns409()
    {
        var job = await SeedAsync(AudioJobStatus.Processing);

        var response = await _client.GetAsync($"/api/audio-jobs/{job.Id}/export?format=md");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Export_OfUnknownJob_Returns404()
    {
        var response = await _client.GetAsync($"/api/audio-jobs/{Guid.NewGuid()}/export?format=md");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Export_FileName_IsSanitized()
    {
        var job = await SeedAsync(AudioJobStatus.Completed, fileName: "..\\evil\"na;me\r\n.mp3");

        var response = await _client.GetAsync($"/api/audio-jobs/{job.Id}/export?format=docx");

        response.Content.Headers.ContentDisposition!.FileNameStar.Should().MatchRegex(@"^[\w .-]+\.docx$").And.StartWith("evil");
    }

    private async Task<string> GetTextAsync(string url)
    {
        var response = await _client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync());
    }

    private async Task<AudioJob> SeedAsync(AudioJobStatus status, string fileName = "meeting.mp3")
    {
        var job = new AudioJob
        {
            OwnerId = TestAuthHandler.DefaultUserId,
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
            db.TranscriptSegments.AddRange(
                new TranscriptSegment { AudioJobId = job.Id, Index = 1, StartMs = 1_500, EndMs = 3_000, Text = "Los geht's.", SpeakerIndex = 0 },
                new TranscriptSegment { AudioJobId = job.Id, Index = 0, StartMs = 0, EndMs = 1_500, Text = "Hallo zusammen.", SpeakerIndex = 0 });
        }
        if (status == AudioJobStatus.Completed)
            db.JobSpeakers.Add(new JobSpeaker { AudioJobId = job.Id, Index = 0, DisplayName = "Anna" });
        await db.SaveChangesAsync();
        return job;
    }

    private async Task<TranscriptVariant> SeedVariantAsync(Guid jobId, PostProcessingMode mode, VariantStatus status, string? text)
    {
        var variant = new TranscriptVariant { AudioJobId = jobId, Mode = mode, Status = status, Text = text };
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TranscriptVariants.Add(variant);
        await db.SaveChangesAsync();
        return variant;
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
