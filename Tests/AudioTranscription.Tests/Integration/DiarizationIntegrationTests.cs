using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AudioTranscription.Api.Configuration;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using AudioTranscription.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace AudioTranscription.Tests.Integration;

/// <summary>S11: speaker diarization — upload flag, segments/subtitles with speaker names, renaming.</summary>
public class DiarizationIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly WebApplicationFactory<Program> _baseFactory;
    private readonly WebApplicationFactory<Program> _factory;

    public DiarizationIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _baseFactory = factory;
        _factory = CreateFactory(factory, diarizationEnabled: true);
    }

    private WebApplicationFactory<Program> CreateFactory(WebApplicationFactory<Program> factory, bool diarizationEnabled)
    {
        var dbName = Guid.NewGuid().ToString();
        return factory.WithWebHostBuilder(builder =>
        {
            if (diarizationEnabled)
            {
                builder.UseSetting("Diarization:Enabled", "true");
                builder.UseSetting("Diarization:SegmentationModelPath", "fake-segmentation.onnx");
                builder.UseSetting("Diarization:EmbeddingModelPath", "fake-embedding.onnx");
            }
            builder.ConfigureTestServices(services =>
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
                if (diarizationEnabled)
                    services.AddSingleton(Mock.Of<IDiarizationService>());
            });
        });
    }

    private async Task<AudioJob> SeedAsync(AudioJobStatus status = AudioJobStatus.Completed)
    {
        var job = new AudioJob
        {
            FileName = "meeting.mp3", ContentType = "audio/mpeg", Model = "Base",
            Status = status, RawTranscript = "Hallo zusammen. Los geht's.",
        };
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AudioJobs.Add(job);
        db.TranscriptSegments.AddRange(
            new TranscriptSegment { AudioJobId = job.Id, Index = 0, StartMs = 0, EndMs = 1_500, Text = "Hallo zusammen.", SpeakerIndex = 0 },
            new TranscriptSegment { AudioJobId = job.Id, Index = 1, StartMs = 1_500, EndMs = 3_000, Text = "Los geht's.", SpeakerIndex = 1 });
        db.JobSpeakers.AddRange(
            new JobSpeaker { AudioJobId = job.Id, Index = 0, DisplayName = "Anna" },
            new JobSpeaker { AudioJobId = job.Id, Index = 1, DisplayName = null });
        await db.SaveChangesAsync();
        return job;
    }

    // --- upload -----------------------------------------------------------

    private static MultipartFormDataContent UploadContent(bool? diarize)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent([.. "ID3"u8.ToArray(), 0, 0, 0, 0]);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        content.Add(file, "file", "meeting.mp3");
        if (diarize is not null)
            content.Add(new StringContent(diarize.Value.ToString()), "diarize");
        return content;
    }

    [Fact]
    public async Task Upload_WithDiarizeTrue_StoresTheRequestOnTheJob()
    {
        var client = _factory.CreateClient();
        using var content = UploadContent(diarize: true);

        var response = await client.PostAsync("/api/audio-jobs", content);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var created = await response.Content.ReadFromJsonAsync<CreateAudioJobResponse>();
        var job = await client.GetFromJsonAsync<AudioJobDto>($"/api/audio-jobs/{created!.Id}");
        job!.DiarizationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task Upload_WithoutDiarize_DefaultsToFalse()
    {
        var client = _factory.CreateClient();
        using var content = UploadContent(diarize: null);

        var response = await client.PostAsync("/api/audio-jobs", content);

        var created = await response.Content.ReadFromJsonAsync<CreateAudioJobResponse>();
        var job = await client.GetFromJsonAsync<AudioJobDto>($"/api/audio-jobs/{created!.Id}");
        job!.DiarizationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task GetTranscriptionOptions_ReportsDiarizationEnabled_WhenConfigured()
    {
        var options = await _factory.CreateClient().GetFromJsonAsync<TranscriptionOptionsDto>("/api/transcription-options");

        options!.DiarizationEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Upload_WithDiarizeTrue_WhenNotConfigured_Returns400()
    {
        using var factory = CreateFactory(_baseFactory, diarizationEnabled: false);
        using var content = UploadContent(diarize: true);

        var response = await factory.CreateClient().PostAsync("/api/audio-jobs", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors.Should().ContainKey("diarize");
    }

    // --- segments and subtitles with speakers ------------------------------

    [Fact]
    public async Task Segments_IncludeSpeakerIndexAndResolvedName()
    {
        var job = await SeedAsync();

        var segments = await _factory.CreateClient().GetFromJsonAsync<List<TranscriptSegmentDto>>($"/api/audio-jobs/{job.Id}/segments");

        segments.Should().Equal(
            new TranscriptSegmentDto(0, 0, 1_500, "Hallo zusammen.", 0, "Anna"),
            new TranscriptSegmentDto(1, 1_500, 3_000, "Los geht's.", 1, "Sprecher 2"));
    }

    [Fact]
    public async Task Subtitles_Srt_PrefixesEachCueWithItsSpeaker()
    {
        var job = await SeedAsync();

        var srt = await _factory.CreateClient().GetStringAsync($"/api/audio-jobs/{job.Id}/subtitles?format=srt");

        srt.Should().Contain("Anna: Hallo zusammen.").And.Contain("Sprecher 2: Los geht's.");
    }

    [Fact]
    public async Task Subtitles_Vtt_WrapsEachCueInAVoiceSpan()
    {
        var job = await SeedAsync();

        var vtt = await _factory.CreateClient().GetStringAsync($"/api/audio-jobs/{job.Id}/subtitles?format=vtt");

        vtt.Should().Contain("<v Anna>Hallo zusammen.</v>").And.Contain("<v Sprecher 2>Los geht's.</v>");
    }

    // --- GET/PUT speakers ----------------------------------------------------

    [Fact]
    public async Task GetSpeakers_ReturnsResolvedNamesForAllDetectedSpeakers()
    {
        var job = await SeedAsync();

        var speakers = await _factory.CreateClient().GetFromJsonAsync<List<JobSpeakerDto>>($"/api/audio-jobs/{job.Id}/speakers");

        speakers.Should().Equal(new JobSpeakerDto(0, "Anna"), new JobSpeakerDto(1, "Sprecher 2"));
    }

    [Fact]
    public async Task GetSpeakers_ForUnknownJob_Returns404()
    {
        var response = await _factory.CreateClient().GetAsync($"/api/audio-jobs/{Guid.NewGuid()}/speakers");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RenameSpeaker_UpdatesTheDisplayName()
    {
        var job = await SeedAsync();
        var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync($"/api/audio-jobs/{job.Id}/speakers/1", new RenameSpeakerRequest("Ben"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var speakers = await client.GetFromJsonAsync<List<JobSpeakerDto>>($"/api/audio-jobs/{job.Id}/speakers");
        speakers.Should().Contain(new JobSpeakerDto(1, "Ben"));
    }

    [Fact]
    public async Task RenameSpeaker_ForUnknownSpeakerIndex_Returns404()
    {
        var job = await SeedAsync();

        var response = await _factory.CreateClient().PutAsJsonAsync($"/api/audio-jobs/{job.Id}/speakers/99", new RenameSpeakerRequest("Ben"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RenameSpeaker_WithEmptyName_Returns400(string name)
    {
        var job = await SeedAsync();

        var response = await _factory.CreateClient().PutAsJsonAsync($"/api/audio-jobs/{job.Id}/speakers/0", new RenameSpeakerRequest(name));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RenameSpeaker_WithNameLongerThan100Characters_Returns400()
    {
        var job = await SeedAsync();

        var response = await _factory.CreateClient().PutAsJsonAsync(
            $"/api/audio-jobs/{job.Id}/speakers/0", new RenameSpeakerRequest(new string('x', 101)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    public void Dispose()
    {
        _factory.Dispose();
        _dir.Dispose();
    }
}
