using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AudioTranscription.Api.Configuration;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Api.Storage;
using AudioTranscription.Infrastructure.Data;
using AudioTranscription.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace AudioTranscription.Tests.Integration;

/// <summary>
/// Model and language selection (S08): configuration, upload parameters and the options endpoint.
/// </summary>
public class TranscriptionSettingsIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly WebApplicationFactory<Program> _baseFactory;
    private readonly WebApplicationFactory<Program> _factory;

    public TranscriptionSettingsIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _baseFactory = factory;
        var transcription = new Mock<ITranscriptionService>();
        // Never completes: the job stays Pending, so the stored settings can be inspected
        transcription
            .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TranscriptionSettings>(), It.IsAny<CancellationToken>()))
            .Returns<string, TranscriptionSettings, CancellationToken>((_, _, ct) =>
                Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => new TranscriptionResult("", null, null), ct));

        _factory = CreateFactory(factory, services => services.AddSingleton(transcription.Object));
    }

    private WebApplicationFactory<Program> CreateFactory(
        WebApplicationFactory<Program> factory, Action<IServiceCollection>? configure = null, Action<IWebHostBuilder>? configureHost = null)
    {
        var dbName = Guid.NewGuid().ToString();
        return factory.WithWebHostBuilder(builder =>
        {
            configureHost?.Invoke(builder);
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
                services.Configure<UploadOptions>(o =>
                {
                    o.TempStoragePath = _dir.Path;
                    o.DeleteAfterTranscription = false;
                });
                configure?.Invoke(services);
            });
        });
    }

    private static MultipartFormDataContent UploadContent(string? model = null, string? language = null)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent([.. "ID3"u8.ToArray(), 0, 0, 0, 0]);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        content.Add(file, "file", "meeting.mp3");
        if (model is not null)
            content.Add(new StringContent(model), "model");
        if (language is not null)
            content.Add(new StringContent(language), "language");
        return content;
    }

    private async Task<AudioJobDto> UploadAndGetJobAsync(string? model, string? language)
    {
        var client = _factory.CreateClient();
        using var content = UploadContent(model, language);
        var response = await client.PostAsync("/api/audio-jobs", content);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var created = await response.Content.ReadFromJsonAsync<CreateAudioJobResponse>();
        return (await client.GetFromJsonAsync<AudioJobDto>($"/api/audio-jobs/{created!.Id}"))!;
    }

    [Fact]
    public async Task Upload_WithModelAndLanguage_StoresCanonicalValues()
    {
        var job = await UploadAndGetJobAsync(model: "small", language: "DE");

        job.Model.Should().Be("Small");
        job.RequestedLanguage.Should().Be("de");
    }

    [Fact]
    public async Task Upload_WithoutModelAndLanguage_UsesDefaultModelAndDetection()
    {
        var job = await UploadAndGetJobAsync(model: null, language: null);

        job.Model.Should().Be("Base");
        job.RequestedLanguage.Should().BeNull();
    }

    [Fact]
    public async Task Upload_WithAutomaticLanguage_StoresNoRequestedLanguage()
    {
        var job = await UploadAndGetJobAsync(model: "Tiny", language: "auto");

        job.Model.Should().Be("Tiny");
        job.RequestedLanguage.Should().BeNull();
    }

    [Theory]
    [InlineData("Huge", null, "model")]
    [InlineData("LargeV3Turbo", null, "model")]   // a Whisper model, but not allowed by default
    [InlineData(null, "xx", "language")]
    [InlineData(null, "german", "language")]
    public async Task Upload_WithUnknownModelOrLanguage_Returns400AndCreatesNoJob(string? model, string? language, string field)
    {
        var client = _factory.CreateClient();
        using var content = UploadContent(model, language);

        var response = await client.PostAsync("/api/audio-jobs", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors.Should().ContainKey(field);
        using var scope = _factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().AudioJobs.CountAsync()).Should().Be(0);
        Directory.EnumerateFiles(_factory.Services.GetRequiredService<ITempFileStore>().StorageDirectory)
            .Should().BeEmpty("a rejected upload is not stored");
    }

    [Fact]
    public async Task GetTranscriptionOptions_ReturnsConfiguredModelsAndLanguages()
    {
        var options = await _factory.CreateClient().GetFromJsonAsync<TranscriptionOptionsDto>("/api/transcription-options");

        options!.DefaultModel.Should().Be("Base");
        options.Models.Should().Equal("Tiny", "Base", "Small", "Medium", "LargeV3");
        options.Languages.Should().Contain(["de", "en"]).And.NotContain("auto").And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void TranscriptionService_IsSingleton_SoLoadedModelsAreShared()
    {
        using var factory = CreateFactory(_baseFactory);

        using var first = factory.Services.CreateScope();
        using var second = factory.Services.CreateScope();

        var service = first.ServiceProvider.GetRequiredService<ITranscriptionService>();
        service.Should().BeOfType<WhisperTranscriptionService>();
        second.ServiceProvider.GetRequiredService<ITranscriptionService>().Should().BeSameAs(service);
    }

    [Fact]
    public void Startup_WithInvalidDefaultModel_Fails()
    {
        using var factory = CreateFactory(_baseFactory,
            configureHost: builder => builder.UseSetting("Whisper:DefaultModel", "Huge"));

        var exception = Record.Exception(() => factory.CreateClient());

        exception.Should().NotBeNull();
        (exception as OptionsValidationException ?? exception!.InnerException as OptionsValidationException ?? exception.GetBaseException())
            .Should().BeOfType<OptionsValidationException>();
    }

    public void Dispose()
    {
        _factory.Dispose();
        _dir.Dispose();
    }
}
