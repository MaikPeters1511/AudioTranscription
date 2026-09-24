using AudioTranscription.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace AudioTranscription.Tests.Data;

public class DatabaseMigrationTests(SqlServerFixture sqlServer) : IClassFixture<SqlServerFixture>
{
    private const string InitialMigration = "20260701082456_InitialCreate";

    [Fact]
    public async Task MigrateWithBaseline_OnNewDatabase_AppliesAllMigrations()
    {
        await using var db = CreateContext();

        await db.Database.MigrateWithBaselineAsync(NullLogger.Instance);

        (await db.Database.GetAppliedMigrationsAsync()).Should().Equal(db.Database.GetMigrations());
        (await db.Users.CountAsync()).Should().Be(0, "the Identity tables exist");
    }

    [Fact]
    public async Task MigrateWithBaseline_OnDatabaseCreatedWithoutMigrations_BaselinesAndKeepsData()
    {
        var connectionString = sqlServer.CreateConnectionString();
        var jobId = Guid.NewGuid();
        await CreateLegacyDatabaseAsync(connectionString, jobId, transcript: "Alter Rohtext");

        await using var db = CreateContext(connectionString);
        await db.Database.MigrateWithBaselineAsync(NullLogger.Instance);

        (await db.Database.GetAppliedMigrationsAsync()).Should().Equal(db.Database.GetMigrations());
        var job = await db.AudioJobs.SingleAsync(j => j.Id == jobId);
        job.RawTranscript.Should().Be("Alter Rohtext", "the old TranscriptText column is renamed, not dropped");
        job.ProcessedTranscript.Should().BeNull();
        (await db.Users.CountAsync()).Should().Be(0, "the Identity tables are added to legacy databases too");
    }

    [Fact]
    public async Task MigrateWithBaseline_WhenAlreadyMigrated_IsIdempotent()
    {
        var connectionString = sqlServer.CreateConnectionString();
        await using (var first = CreateContext(connectionString))
            await first.Database.MigrateWithBaselineAsync(NullLogger.Instance);

        await using var db = CreateContext(connectionString);
        var exception = await Record.ExceptionAsync(() => db.Database.MigrateWithBaselineAsync(NullLogger.Instance));

        exception.Should().BeNull();
        (await db.Database.GetAppliedMigrationsAsync()).Should().Equal(db.Database.GetMigrations());
    }

    /// <summary>
    /// Recreates the state of a database created by the former EnsureCreated call:
    /// the InitialCreate schema without a __EFMigrationsHistory table.
    /// </summary>
    private async Task CreateLegacyDatabaseAsync(string connectionString, Guid jobId, string transcript)
    {
        await using var db = CreateContext(connectionString);
        await db.GetService<IMigrator>().MigrateAsync(InitialMigration);
        await db.Database.ExecuteSqlRawAsync("DROP TABLE [__EFMigrationsHistory]");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [AudioJobs] ([Id], [FileName], [FileSizeBytes], [ContentType], [Status], [TranscriptText], [CreatedAtUtc])
            VALUES ({jobId}, 'legacy.mp3', 1024, 'audio/mpeg', 2, {transcript}, SYSUTCDATETIME())
            """);
    }

    private AppDbContext CreateContext(string? connectionString = null) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString ?? sqlServer.CreateConnectionString())
            .Options);
}
