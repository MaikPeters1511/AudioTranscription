using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AudioTranscription.Api.Configuration;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Api.Storage;
using AudioTranscription.Infrastructure.Data;
using AudioTranscription.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace AudioTranscription.Tests.Integration;

public class UploadStorageIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly WebApplicationFactory<Program> _factory;

    public UploadStorageIntegrationTests(WebApplicationFactory<Program> factory)
    {
        var dbName = Guid.NewGuid().ToString();
        var transcription = new Mock<ITranscriptionService>();
        transcription
            .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TranscriptionSettings>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranscriptionResult("Hallo", "de", 1));

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
            services.AddTestAuthentication();
            services.Configure<UploadOptions>(o =>
            {
                o.TempStoragePath = _dir.Path;
                o.DeleteAfterTranscription = false; // keep the upload so it can be inspected
            });
        }));
    }

    [Fact]
    public async Task Upload_StoresFileAtPathDerivableFromJob()
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent([.. "ID3"u8.ToArray(), 0, 0, 0, 0]);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        content.Add(file, "file", "Meeting.MP3");

        var response = await _factory.CreateClient().PostAsync("/api/audio-jobs", content);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var created = await response.Content.ReadFromJsonAsync<CreateAudioJobResponse>();
        var store = _factory.Services.GetRequiredService<ITempFileStore>();
        File.Exists(store.GetUploadPath(created!.Id, "Meeting.MP3")).Should().BeTrue();
    }

    [Fact]
    public async Task Upload_WithWebmCodecsParameter_IsAccepted()
    {
        // MediaRecorder in Chrome/Firefox sends "audio/webm;codecs=opus" (S12)
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent([0x1A, 0x45, 0xDF, 0xA3, 0, 0, 0, 0]);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/webm") { Parameters = { new NameValueHeaderValue("codecs", "opus") } };
        content.Add(file, "file", "recording.webm");

        var response = await _factory.CreateClient().PostAsync("/api/audio-jobs", content);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var created = await response.Content.ReadFromJsonAsync<CreateAudioJobResponse>();
        using var scope = _factory.Services.CreateScope();
        var job = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AudioJobs.FindAsync(created!.Id);
        job!.ContentType.Should().Be("audio/webm");
    }

    [Fact]
    public async Task Upload_WithAShortMp4Video_IsAccepted()
    {
        // S14: only the audio track is used (ffmpeg "-vn"); an ftyp box is enough to pass validation here
        using var content = new MultipartFormDataContent();
        var ftypBox = new byte[] { 0x00, 0x00, 0x00, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0, 0, 0, 0 };
        var file = new ByteArrayContent(ftypBox);
        file.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        content.Add(file, "file", "clip.mp4");

        var response = await _factory.CreateClient().PostAsync("/api/audio-jobs", content);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var created = await response.Content.ReadFromJsonAsync<CreateAudioJobResponse>();
        using var scope = _factory.Services.CreateScope();
        var job = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AudioJobs.FindAsync(created!.Id);
        job!.ContentType.Should().Be("video/mp4");
    }

    public void Dispose()
    {
        _factory.Dispose();
        _dir.Dispose();
    }
}

/// <summary>S14-T1: the configured size limit is enforced exactly at the boundary.</summary>
public class UploadSizeLimitIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const int LimitBytes = 1_000;
    private readonly TempDirectory _dir = new();
    private readonly WebApplicationFactory<Program> _factory;

    public UploadSizeLimitIntegrationTests(WebApplicationFactory<Program> factory)
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
            services.Configure<UploadOptions>(o =>
            {
                o.TempStoragePath = _dir.Path;
                o.MaxFileSizeBytes = LimitBytes;
            });
        }));
    }

    private static MultipartFormDataContent Mp3Content(int fileSizeBytes)
    {
        var bytes = new byte[fileSizeBytes];
        "ID3"u8.ToArray().CopyTo(bytes, 0);
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        content.Add(file, "file", "big.mp3");
        return content;
    }

    [Fact]
    public async Task Upload_JustUnderTheLimit_IsAccepted()
    {
        using var content = Mp3Content(LimitBytes);

        var response = await _factory.CreateClient().PostAsync("/api/audio-jobs", content);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Upload_JustOverTheLimit_Returns413()
    {
        using var content = Mp3Content(LimitBytes + 1);

        var response = await _factory.CreateClient().PostAsync("/api/audio-jobs", content);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    public void Dispose()
    {
        _factory.Dispose();
        _dir.Dispose();
    }
}
