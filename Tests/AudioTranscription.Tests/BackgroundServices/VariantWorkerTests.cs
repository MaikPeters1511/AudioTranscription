using AudioTranscription.Api.BackgroundServices;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Api.Hubs;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using AudioTranscription.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudioTranscription.Tests.BackgroundServices;

public class VariantWorkerTests : IDisposable
{
    private readonly Mock<IVariantGenerator> _generator = new();
    private readonly Mock<ILogger<VariantWorker>> _logger = new();
    private readonly Mock<IClientProxy> _clientProxy = new();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private ServiceProvider? _provider;

    private async Task<(VariantWorker Worker, Guid VariantId)> ArrangeAsync(
        PostProcessingMode mode = PostProcessingMode.Cleanup,
        string? targetLanguage = null,
        VariantStatus status = VariantStatus.Pending,
        string? rawTranscript = "Hallo Welt")
    {
        var hubClients = new Mock<IHubClients>();
        hubClients.Setup(c => c.All).Returns(_clientProxy.Object);
        var hubContext = new Mock<IHubContext<TranscriptionHub>>();
        hubContext.Setup(h => h.Clients).Returns(hubClients.Object);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddSingleton(_generator.Object);
        services.AddSingleton(hubContext.Object);
        _provider = services.BuildServiceProvider();

        var jobId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AudioJobs.Add(new AudioJob
            {
                Id = jobId, FileName = "meeting.mp3", ContentType = "audio/mpeg",
                Status = AudioJobStatus.Completed, Model = "Base", RawTranscript = rawTranscript
            });
            db.TranscriptVariants.Add(new TranscriptVariant
            {
                Id = variantId, AudioJobId = jobId, Mode = mode, TargetLanguage = targetLanguage, Status = status
            });
            await db.SaveChangesAsync();
        }

        var worker = new VariantWorker(new VariantQueue(), _provider.GetRequiredService<IServiceScopeFactory>(), _logger.Object);
        return (worker, variantId);
    }

    private async Task<TranscriptVariant> GetVariantAsync(Guid id)
    {
        using var scope = _provider!.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().TranscriptVariants.AsNoTracking().SingleAsync(v => v.Id == id);
    }

    [Fact]
    public async Task ProcessVariant_WhenGenerationSucceeds_StoresTextAndCompletes()
    {
        var (worker, variantId) = await ArrangeAsync();
        _generator.Setup(g => g.GenerateAsync(PostProcessingMode.Cleanup, "Hallo Welt", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Hallo, Welt!");

        await worker.ProcessVariantAsync(new VariantJobRequest(variantId), CancellationToken.None);

        var variant = await GetVariantAsync(variantId);
        variant.Status.Should().Be(VariantStatus.Completed);
        variant.Text.Should().Be("Hallo, Welt!");
        variant.CompletedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessVariant_PassesTargetLanguageToTheGenerator()
    {
        var (worker, variantId) = await ArrangeAsync(mode: PostProcessingMode.Translate, targetLanguage: "fr");
        _generator.Setup(g => g.GenerateAsync(PostProcessingMode.Translate, "Hallo Welt", "fr", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Bonjour le monde");

        await worker.ProcessVariantAsync(new VariantJobRequest(variantId), CancellationToken.None);

        (await GetVariantAsync(variantId)).Text.Should().Be("Bonjour le monde");
    }

    [Fact]
    public async Task ProcessVariant_WhenGenerationFails_MarksFailedAndKeepsNoText()
    {
        var (worker, variantId) = await ArrangeAsync();
        _generator.Setup(g => g.GenerateAsync(It.IsAny<PostProcessingMode>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Ollama nicht erreichbar"));

        await worker.ProcessVariantAsync(new VariantJobRequest(variantId), CancellationToken.None);

        var variant = await GetVariantAsync(variantId);
        variant.Status.Should().Be(VariantStatus.Failed);
        variant.Text.Should().BeNull();
        variant.ErrorMessage.Should().Contain("Ollama nicht erreichbar");
    }

    [Fact]
    public async Task ProcessVariant_PushesVariantCompletedEvent()
    {
        var (worker, variantId) = await ArrangeAsync();
        _generator.Setup(g => g.GenerateAsync(It.IsAny<PostProcessingMode>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Ergebnis");

        await worker.ProcessVariantAsync(new VariantJobRequest(variantId), CancellationToken.None);

        var variant = await GetVariantAsync(variantId);
        _clientProxy.Verify(c => c.SendCoreAsync(
            "VariantCompleted",
            It.Is<object?[]>(args => args.Length == 1 && args[0]!.Equals(
                new VariantCompletedDto(variant.AudioJobId, variantId, PostProcessingMode.Cleanup, VariantStatus.Completed))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(VariantStatus.Completed)]
    [InlineData(VariantStatus.Failed)]
    public async Task ProcessVariant_WhenAlreadyFinished_DoesNotCallTheGeneratorAgain(VariantStatus status)
    {
        var (worker, variantId) = await ArrangeAsync(status: status);

        await worker.ProcessVariantAsync(new VariantJobRequest(variantId), CancellationToken.None);

        _generator.Verify(g => g.GenerateAsync(It.IsAny<PostProcessingMode>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessVariant_WhenNotFound_DoesNothing()
    {
        var (worker, _) = await ArrangeAsync();

        var exception = await Record.ExceptionAsync(() => worker.ProcessVariantAsync(new VariantJobRequest(Guid.NewGuid()), CancellationToken.None));

        exception.Should().BeNull();
    }

    public void Dispose() => _provider?.Dispose();
}
