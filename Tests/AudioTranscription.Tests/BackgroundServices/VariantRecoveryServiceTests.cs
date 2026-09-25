using AudioTranscription.Api.BackgroundServices;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AudioTranscription.Tests.BackgroundServices;

public class VariantRecoveryServiceTests : IDisposable
{
    private readonly VariantQueue _queue = new();
    private readonly ServiceProvider _provider;

    public VariantRecoveryServiceTests()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        _provider = services.BuildServiceProvider();
    }

    private VariantRecoveryService CreateSut() =>
        new(_provider.GetRequiredService<IServiceScopeFactory>(), _queue, NullLogger<VariantRecoveryService>.Instance);

    private List<VariantJobRequest> DrainQueue()
    {
        var result = new List<VariantJobRequest>();
        while (_queue.TryRead(out var request))
            result.Add(request);
        return result;
    }

    private async Task<TranscriptVariant> AddVariantAsync(VariantStatus status, DateTime createdAtUtc)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = new AudioJob { FileName = "a.mp3", ContentType = "audio/mpeg", Model = "Base" };
        var variant = new TranscriptVariant { AudioJobId = job.Id, Mode = PostProcessingMode.Summary, Status = status, CreatedAtUtc = createdAtUtc };
        db.AudioJobs.Add(job);
        db.TranscriptVariants.Add(variant);
        await db.SaveChangesAsync();
        return variant;
    }

    [Fact]
    public async Task Recovery_EnqueuesOnlyPendingVariantsOrderedByCreation()
    {
        var now = DateTime.UtcNow;
        var second = await AddVariantAsync(VariantStatus.Pending, now.AddMinutes(-1));
        var completed = await AddVariantAsync(VariantStatus.Completed, now);
        var failed = await AddVariantAsync(VariantStatus.Failed, now);
        var first = await AddVariantAsync(VariantStatus.Pending, now.AddMinutes(-2));

        await CreateSut().StartAsync(CancellationToken.None);

        DrainQueue().Select(r => r.VariantId).Should().Equal(first.Id, second.Id);
        _ = completed;
        _ = failed;
    }

    [Fact]
    public async Task Recovery_WithNoPendingVariants_EnqueuesNothing()
    {
        await CreateSut().StartAsync(CancellationToken.None);

        DrainQueue().Should().BeEmpty();
    }

    public void Dispose() => _provider.Dispose();
}
