using System.Net;
using System.Net.Http.Json;
using AudioTranscription.Api.Configuration;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Api.Storage;
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

/// <summary>S10: on-demand transcript variants (cleanup/summary/bullet points/action items/translate).</summary>
public class VariantIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly WebApplicationFactory<Program> _baseFactory;
    private readonly WebApplicationFactory<Program> _factory;

    public VariantIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _baseFactory = factory;
        _factory = CreateFactory(factory, services =>
        {
            services.AddSingleton(Mock.Of<IVariantGenerator>());
            services.AddSingleton<AudioTranscription.Api.BackgroundServices.VariantQueue>();
        });
    }

    private WebApplicationFactory<Program> CreateFactory(WebApplicationFactory<Program> factory, Action<IServiceCollection>? configure = null)
    {
        var dbName = Guid.NewGuid().ToString();
        return factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
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
            configure?.Invoke(services);
        }));
    }

    private async Task<AudioJob> SeedAsync(AudioJobStatus status = AudioJobStatus.Completed, string? rawTranscript = "Hallo Welt")
    {
        var job = new AudioJob
        {
            FileName = "meeting.mp3", ContentType = "audio/mpeg", Model = "Base",
            Status = status, RawTranscript = status == AudioJobStatus.Completed ? rawTranscript : null
        };
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AudioJobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }

    // --- POST /variants ---------------------------------------------------

    [Fact]
    public async Task CreateVariant_ForCompletedJob_Returns202AndQueuesAPendingVariant()
    {
        var job = await SeedAsync();
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/audio-jobs/{job.Id}/variants",
            new CreateVariantRequest(PostProcessingMode.Summary, null));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<TranscriptVariantDto>();
        body!.Mode.Should().Be(PostProcessingMode.Summary);
        body.Status.Should().Be(VariantStatus.Pending);

        using var scope = _factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().TranscriptVariants.SingleAsync();
        stored.Status.Should().Be(VariantStatus.Pending);
    }

    [Fact]
    public async Task CreateVariant_Translate_RequiresASupportedTargetLanguage()
    {
        var job = await SeedAsync();
        var client = _factory.CreateClient();

        var missing = await client.PostAsJsonAsync($"/api/audio-jobs/{job.Id}/variants",
            new CreateVariantRequest(PostProcessingMode.Translate, null));
        var unsupported = await client.PostAsJsonAsync($"/api/audio-jobs/{job.Id}/variants",
            new CreateVariantRequest(PostProcessingMode.Translate, "xx"));
        var ok = await client.PostAsJsonAsync($"/api/audio-jobs/{job.Id}/variants",
            new CreateVariantRequest(PostProcessingMode.Translate, "FR"));

        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        unsupported.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ok.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await ok.Content.ReadFromJsonAsync<TranscriptVariantDto>())!.TargetLanguage.Should().Be("fr");
    }

    [Fact]
    public async Task CreateVariant_WithUnknownMode_Returns400()
    {
        var job = await SeedAsync();

        var response = await _factory.CreateClient().PostAsJsonAsync($"/api/audio-jobs/{job.Id}/variants",
            new CreateVariantRequest((PostProcessingMode)99, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateVariant_ForUnknownJob_Returns404()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync($"/api/audio-jobs/{Guid.NewGuid()}/variants",
            new CreateVariantRequest(PostProcessingMode.Cleanup, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(AudioJobStatus.Pending)]
    [InlineData(AudioJobStatus.Processing)]
    [InlineData(AudioJobStatus.Failed)]
    public async Task CreateVariant_ForUnfinishedJob_Returns409(AudioJobStatus status)
    {
        var job = await SeedAsync(status);

        var response = await _factory.CreateClient().PostAsJsonAsync($"/api/audio-jobs/{job.Id}/variants",
            new CreateVariantRequest(PostProcessingMode.Cleanup, null));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateVariant_WhenOllamaIsNotConfigured_Returns503()
    {
        using var factory = CreateFactory(_baseFactory); // no IVariantGenerator registered
        var job = await SeedFor(factory);

        var response = await factory.CreateClient().PostAsJsonAsync($"/api/audio-jobs/{job.Id}/variants",
            new CreateVariantRequest(PostProcessingMode.Cleanup, null));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    private async Task<AudioJob> SeedFor(WebApplicationFactory<Program> factory)
    {
        var job = new AudioJob { FileName = "meeting.mp3", ContentType = "audio/mpeg", Model = "Base", Status = AudioJobStatus.Completed, RawTranscript = "Hallo" };
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AudioJobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }

    [Fact]
    public async Task CreateVariant_RunningItAgain_OverwritesTheExistingVariantInsteadOfAddingASecondOne()
    {
        var job = await SeedAsync();
        var client = _factory.CreateClient();
        var first = await client.PostAsJsonAsync($"/api/audio-jobs/{job.Id}/variants", new CreateVariantRequest(PostProcessingMode.BulletPoints, null));
        var firstId = (await first.Content.ReadFromJsonAsync<TranscriptVariantDto>())!.Id;

        var second = await client.PostAsJsonAsync($"/api/audio-jobs/{job.Id}/variants", new CreateVariantRequest(PostProcessingMode.BulletPoints, null));

        (await second.Content.ReadFromJsonAsync<TranscriptVariantDto>())!.Id.Should().Be(firstId);
        using var scope = _factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().TranscriptVariants.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateVariant_CleanupAndTranslate_AreDistinctVariants()
    {
        var job = await SeedAsync();
        var client = _factory.CreateClient();

        await client.PostAsJsonAsync($"/api/audio-jobs/{job.Id}/variants", new CreateVariantRequest(PostProcessingMode.Cleanup, null));
        await client.PostAsJsonAsync($"/api/audio-jobs/{job.Id}/variants", new CreateVariantRequest(PostProcessingMode.Translate, "en"));
        await client.PostAsJsonAsync($"/api/audio-jobs/{job.Id}/variants", new CreateVariantRequest(PostProcessingMode.Translate, "de"));

        using var scope = _factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().TranscriptVariants.CountAsync()).Should().Be(3);
    }

    // --- GET /variants ------------------------------------------------------

    [Fact]
    public async Task GetVariants_ReturnsAllVariantsOfTheJob()
    {
        var job = await SeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TranscriptVariants.Add(new TranscriptVariant { AudioJobId = job.Id, Mode = PostProcessingMode.Summary, Status = VariantStatus.Completed, Text = "Kurzfassung" });
            await db.SaveChangesAsync();
        }

        var variants = await _factory.CreateClient().GetFromJsonAsync<List<TranscriptVariantDto>>($"/api/audio-jobs/{job.Id}/variants");

        variants.Should().ContainSingle(v => v.Mode == PostProcessingMode.Summary && v.Text == "Kurzfassung");
    }

    [Fact]
    public async Task GetVariants_ForUnknownJob_Returns404()
    {
        var response = await _factory.CreateClient().GetAsync($"/api/audio-jobs/{Guid.NewGuid()}/variants");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- retry removes stale variants (S09 x S10) ---------------------------

    [Fact]
    public async Task RetryingAJob_RemovesItsExistingVariants()
    {
        var job = await SeedAsync(AudioJobStatus.Failed, rawTranscript: null);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TranscriptVariants.Add(new TranscriptVariant { AudioJobId = job.Id, Mode = PostProcessingMode.Cleanup, Status = VariantStatus.Completed, Text = "Alt" });
            await db.SaveChangesAsync();
        }
        _dir.CreateFile($"{job.Id}.mp3");

        await _factory.CreateClient().PostAsync($"/api/audio-jobs/{job.Id}/retry", null);

        using var check = _factory.Services.CreateScope();
        (await check.ServiceProvider.GetRequiredService<AppDbContext>().TranscriptVariants.CountAsync()).Should().Be(0);
    }

    public void Dispose()
    {
        _factory.Dispose();
        _dir.Dispose();
    }
}
