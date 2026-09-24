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

    public void Dispose()
    {
        _factory.Dispose();
        _dir.Dispose();
    }
}
