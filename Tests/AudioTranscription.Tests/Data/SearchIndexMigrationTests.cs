using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AudioTranscription.Tests.Data;

/// <summary>
/// S13-T2: the full-text index (ADR 0005) is created by the migration and actually finds rows,
/// against a real SQL Server (Testcontainers), the same way it runs under Aspire/docker-compose.
/// </summary>
public class SearchIndexMigrationTests(SearchSqlServerFixture sqlServer) : IClassFixture<SearchSqlServerFixture>
{
    [Fact]
    public async Task Migration_CreatesAWorkingFullTextIndex_OnAllThreeTranscriptSources()
    {
        var connectionString = sqlServer.CreateConnectionString();
        var jobId = Guid.NewGuid();
        await using (var db = CreateContext(connectionString))
        {
            await db.Database.MigrateWithBaselineAsync(NullLogger.Instance);

            db.AudioJobs.Add(new AudioJob
            {
                Id = jobId, FileName = "meeting.mp3", ContentType = "audio/mpeg", Model = "Base",
                Status = AudioJobStatus.Completed, RawTranscript = "Der Zauberer wanderte durch den Wald.",
            });
            db.TranscriptSegments.Add(new TranscriptSegment
            {
                AudioJobId = jobId, Index = 0, StartMs = 0, EndMs = 1000, Text = "Ein Kobold lauerte hinter dem Baum.",
            });
            db.TranscriptVariants.Add(new TranscriptVariant
            {
                AudioJobId = jobId, Mode = PostProcessingMode.Cleanup, Status = VariantStatus.Completed,
                Text = "Zusammenfassung: Drachen bewachen die Höhle.",
            });
            await db.SaveChangesAsync();

            await WaitForFullTextPopulationAsync(db, "AudioJobs");
            await WaitForFullTextPopulationAsync(db, "TranscriptSegments");
            await WaitForFullTextPopulationAsync(db, "TranscriptVariants");
        }

        await using var query = CreateContext(connectionString);
        var jobHit = await query.AudioJobs.FromSqlInterpolated(
            $"SELECT * FROM [AudioJobs] WHERE CONTAINS([RawTranscript], '\"Zauberer\"')").ToListAsync();
        var segmentHit = await query.TranscriptSegments.FromSqlInterpolated(
            $"SELECT * FROM [TranscriptSegments] WHERE CONTAINS([Text], '\"Kobold\"')").ToListAsync();
        var variantHit = await query.TranscriptVariants.FromSqlInterpolated(
            $"SELECT * FROM [TranscriptVariants] WHERE CONTAINS([Text], '\"Drachen\"')").ToListAsync();

        jobHit.Should().ContainSingle().Which.Id.Should().Be(jobId);
        segmentHit.Should().ContainSingle();
        variantHit.Should().ContainSingle();
    }

    [Fact]
    public async Task Migration_SupportsGermanStemming()
    {
        var connectionString = sqlServer.CreateConnectionString();
        var jobId = Guid.NewGuid();
        await using (var db = CreateContext(connectionString))
        {
            await db.Database.MigrateWithBaselineAsync(NullLogger.Instance);
            db.AudioJobs.Add(new AudioJob
            {
                Id = jobId, FileName = "meeting.mp3", ContentType = "audio/mpeg", Model = "Base",
                Status = AudioJobStatus.Completed, RawTranscript = "Die Kinder spielten fröhlich im Garten.",
            });
            await db.SaveChangesAsync();
            await WaitForFullTextPopulationAsync(db, "AudioJobs");
        }

        // FREETEXT applies German stemming: "spielen" (infinitive) should still find "spielten" (past tense)
        await using var query = CreateContext(connectionString);
        var hit = await query.AudioJobs.FromSqlInterpolated(
            $"SELECT * FROM [AudioJobs] WHERE FREETEXT([RawTranscript], 'spielen')").ToListAsync();

        hit.Should().ContainSingle().Which.Id.Should().Be(jobId);
    }

    /// <summary>Full-text population runs in the background after CREATE/INSERT; tests must wait for it.</summary>
    private static async Task WaitForFullTextPopulationAsync(AppDbContext db, string tableName)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var status = await db.Database.SqlQuery<int>(
                $"SELECT OBJECTPROPERTYEX(OBJECT_ID({tableName}), 'TableFulltextPopulateStatus') AS Value")
                .SingleAsync();
            if (status == 0)
                return;
            await Task.Delay(200);
        }
        throw new TimeoutException($"Full-text population on {tableName} did not finish in time.");
    }

    private AppDbContext CreateContext(string? connectionString = null) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString ?? sqlServer.CreateConnectionString())
            .Options);
}
