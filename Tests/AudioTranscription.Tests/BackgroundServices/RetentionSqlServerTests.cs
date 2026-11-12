using AudioTranscription.Api.Access;
using AudioTranscription.Api.BackgroundServices;
using AudioTranscription.Api.Configuration;
using AudioTranscription.Api.Storage;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using AudioTranscription.Tests.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AudioTranscription.Tests.BackgroundServices;

/// <summary>
/// S30-F1: retention against a real SQL Server. The service tests run on the in-memory database, which cannot show that the
/// conditional <c>ExecuteDelete</c> translates as intended, that the foreign keys remove every dependent row, or that deleted
/// transcripts leave the full-text index.
/// </summary>
public class RetentionSqlServerTests : IClassFixture<SearchSqlServerFixture>, IDisposable
{
    private static readonly DateTime Now = new(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc);
    private const string Owner = "owner-id";
    private const string Reader = "reader-id";
    private const string Word = "Zauberer";

    private readonly SearchSqlServerFixture _sqlServer;
    private readonly TempDirectory _dir = new();
    private string _connectionString = null!;

    public RetentionSqlServerTests(SearchSqlServerFixture sqlServer) => _sqlServer = sqlServer;

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(_connectionString).Options);

    private async Task<RetentionService> CreateAsync(int transcriptDays, int audioDays = 0, int maxJobsPerRun = 500)
    {
        _connectionString = _sqlServer.CreateConnectionString();
        await using (var db = NewDb())
        {
            await db.Database.MigrateWithBaselineAsync(NullLogger.Instance);
            // Jobs belong to users (S19): the foreign keys need real rows
            foreach (var id in new[] { Owner, Reader })
                db.Users.Add(new IdentityUser { Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant() });
            await db.SaveChangesAsync();
        }

        var notifier = new Mock<IJobNotifier>();
        notifier.Setup(n => n.GetRecipientsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([Owner]);
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(_connectionString));
        services.AddSingleton(notifier.Object);
        var provider = services.BuildServiceProvider();

        return new RetentionService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new TempFileStore(Options.Create(new UploadOptions { TempStoragePath = _dir.Path, DeleteAfterTranscription = false })),
            Options.Create(new RetentionOptions { TranscriptDays = transcriptDays, AudioDays = audioDays, MaxJobsPerRun = maxJobsPerRun }),
            new FixedTimeProvider(Now),
            NullLogger<RetentionService>.Instance);
    }

    /// <summary>A job with a segment (containing the search word) and one row in every table that depends on a job.</summary>
    private async Task<Guid> AddJobAsync(
        double ageDays, AudioJobStatus status = AudioJobStatus.Completed, string? ownerId = Owner, bool locked = false, bool noCompletionTime = false)
    {
        await using var db = NewDb();
        var job = new AudioJob
        {
            OwnerId = ownerId, FileName = "a.mp3", ContentType = "audio/mpeg", Model = "Base", Status = status, RetentionLocked = locked,
            RawTranscript = $"Der {Word} sprach.", CreatedAtUtc = Now.AddDays(-ageDays - 1),
            CompletedAtUtc = noCompletionTime ? null : Now.AddDays(-ageDays),
        };
        db.AudioJobs.Add(job);
        var segment = new TranscriptSegment { AudioJobId = job.Id, Index = 0, StartMs = 0, EndMs = 1_000, Text = $"Der {Word} sprach." };
        db.TranscriptSegments.Add(segment);
        db.SegmentEdits.Add(new SegmentEdit { SegmentId = segment.Id, Version = 1, Text = $"Der {Word} sprach laut.", EditedBy = Owner });
        db.TranscriptVariants.Add(new TranscriptVariant { AudioJobId = job.Id, Mode = PostProcessingMode.Summary, Status = VariantStatus.Completed, Text = $"Ein {Word}." });
        db.JobSpeakers.Add(new JobSpeaker { AudioJobId = job.Id, Index = 0, DisplayName = "Anna" });
        db.TranscriptChapters.Add(new TranscriptChapter { AudioJobId = job.Id, Position = 0, StartMs = 0, EndMs = 1_000, Title = "Kapitel" });
        db.ChapterRuns.Add(new ChapterRun { AudioJobId = job.Id, Status = VariantStatus.Completed });
        if (ownerId is not null)
        {
            db.JobShares.Add(new JobShare { AudioJobId = job.Id, UserId = Reader });
            db.JobFavorites.Add(new JobFavorite { AudioJobId = job.Id, UserId = ownerId });
            var tag = new Tag { OwnerId = ownerId, Name = "T" + job.Id.ToString("N")[..8] };
            db.Tags.Add(tag);
            db.JobTags.Add(new JobTag { AudioJobId = job.Id, TagId = tag.Id });
        }
        await db.SaveChangesAsync();
        return job.Id;
    }

    private async Task<int> DependentRowsAsync(Guid jobId)
    {
        await using var db = NewDb();
        var segmentIds = await db.TranscriptSegments.Where(s => s.AudioJobId == jobId).Select(s => s.Id).ToListAsync();
        return await db.AudioJobs.CountAsync(j => j.Id == jobId)
            + segmentIds.Count
            + await db.SegmentEdits.CountAsync(e => segmentIds.Contains(e.SegmentId))
            + await db.TranscriptVariants.CountAsync(v => v.AudioJobId == jobId)
            + await db.JobSpeakers.CountAsync(s => s.AudioJobId == jobId)
            + await db.TranscriptChapters.CountAsync(c => c.AudioJobId == jobId)
            + await db.ChapterRuns.CountAsync(r => r.AudioJobId == jobId)
            + await db.JobShares.CountAsync(s => s.AudioJobId == jobId)
            + await db.JobFavorites.CountAsync(f => f.AudioJobId == jobId)
            + await db.JobTags.CountAsync(t => t.AudioJobId == jobId);
    }

    private async Task<bool> ExistsAsync(Guid jobId)
    {
        await using var db = NewDb();
        return await db.AudioJobs.AnyAsync(j => j.Id == jobId);
    }

    private static async Task WaitForFullTextPopulationAsync(AppDbContext db)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var status = 0;
            foreach (var table in new[] { "AudioJobs", "TranscriptSegments", "TranscriptVariants" })
            {
                status += await db.Database.SqlQuery<int>(
                    $"SELECT OBJECTPROPERTYEX(OBJECT_ID({table}), 'TableFulltextPopulateStatus') AS Value").SingleAsync();
            }
            if (status == 0)
                return;
            await Task.Delay(200);
        }
        throw new TimeoutException("Full-text population did not finish in time.");
    }

    [Fact]
    public async Task ExpiredJob_IsDeleted_WithEveryDependentRow_AndOtherJobsStay()
    {
        var sut = await CreateAsync(transcriptDays: 30);
        var expired = await AddJobAsync(ageDays: 40);
        var expiredFailed = await AddJobAsync(ageDays: 50, AudioJobStatus.Failed);
        var recent = await AddJobAsync(ageDays: 5);
        var locked = await AddJobAsync(ageDays: 400, locked: true);
        var unowned = await AddJobAsync(ageDays: 400, ownerId: null);
        var open = await AddJobAsync(ageDays: 400, AudioJobStatus.Processing);
        var rowsOfKept = new Dictionary<Guid, int>();
        foreach (var id in new[] { recent, locked, unowned, open })
            rowsOfKept[id] = await DependentRowsAsync(id);

        await sut.RunOnceAsync(CancellationToken.None);

        (await DependentRowsAsync(expired)).Should().Be(0, "the foreign keys remove segments, edits, variants, speakers, chapters, shares, favorites and tag links");
        (await DependentRowsAsync(expiredFailed)).Should().Be(0);
        foreach (var (id, rows) in rowsOfKept)
            (await DependentRowsAsync(id)).Should().Be(rows, "a job that is recent, locked, ownerless or still open is untouched");
        rowsOfKept.Values.Should().OnlyContain(r => r > 5);
    }

    [Fact]
    public async Task DeletedTranscripts_LeaveTheFullTextSearch()
    {
        var sut = await CreateAsync(transcriptDays: 30);
        var expired = await AddJobAsync(ageDays: 40);
        var kept = await AddJobAsync(ageDays: 5);
        await using (var db = NewDb())
            await WaitForFullTextPopulationAsync(db);

        await sut.RunOnceAsync(CancellationToken.None);

        await using var search = NewDb();
        await WaitForFullTextPopulationAsync(search);
        var segmentJobs = await search.TranscriptSegments.Where(s => EF.Functions.FreeText(s.Text, Word)).Select(s => s.AudioJobId).ToListAsync();
        var variantJobs = await search.TranscriptVariants.Where(v => EF.Functions.FreeText(v.Text!, Word)).Select(v => v.AudioJobId).ToListAsync();
        var rawJobs = await search.AudioJobs.Where(j => EF.Functions.FreeText(j.RawTranscript!, Word)).Select(j => j.Id).ToListAsync();
        segmentJobs.Should().Equal(kept);
        variantJobs.Should().Equal(kept);
        rawJobs.Should().Equal(kept);
        rawJobs.Should().NotContain(expired);
    }

    [Fact]
    public async Task TheCondition_TranslatesAsExpected_AtTheCutoffAndWithoutACompletionTime()
    {
        var sut = await CreateAsync(transcriptDays: 30);
        var atCutoff = await AddJobAsync(ageDays: 30);                                   // exactly at the cutoff: kept (strictly older is deleted)
        var oneSecondOlder = await AddJobAsync(ageDays: 30 + 1.0 / 86_400);               // one second older: deleted
        var noCompletion = await AddJobAsync(ageDays: 0, noCompletionTime: true);         // judged by its creation time, which is a day before "age"
        await using (var db = NewDb())
        {
            var job = await db.AudioJobs.SingleAsync(j => j.Id == noCompletion);
            job.CreatedAtUtc = Now.AddDays(-45);
            await db.SaveChangesAsync();
        }

        await sut.RunOnceAsync(CancellationToken.None);

        (await ExistsAsync(atCutoff)).Should().BeTrue();
        (await ExistsAsync(oneSecondOlder)).Should().BeFalse();
        (await ExistsAsync(noCompletion)).Should().BeFalse("without a completion time the creation time counts");
    }

    [Fact]
    public async Task TheLimitPerRun_DeletesTheOldestFirst_AndTheRestFollows()
    {
        var sut = await CreateAsync(transcriptDays: 30, maxJobsPerRun: 2);
        var oldest = await AddJobAsync(ageDays: 400);
        var middle = await AddJobAsync(ageDays: 300);
        var newest = await AddJobAsync(ageDays: 40);

        await sut.RunOnceAsync(CancellationToken.None);
        (await ExistsAsync(oldest)).Should().BeFalse();
        (await ExistsAsync(middle)).Should().BeFalse();
        (await ExistsAsync(newest)).Should().BeTrue();

        await sut.RunOnceAsync(CancellationToken.None);
        (await ExistsAsync(newest)).Should().BeFalse();
    }

    public void Dispose() => _dir.Dispose();
}
