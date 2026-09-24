using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace AudioTranscription.Infrastructure.Data;

public static class DatabaseMigrationExtensions
{
    /// <summary>
    /// Schema that databases created by the former <c>EnsureCreated</c> call already have.
    /// </summary>
    public const string BaselineMigrationId = "20260701082456_InitialCreate";

    /// <summary>
    /// Applies pending EF Core migrations. Databases that were created with <c>EnsureCreated</c>
    /// (tables exist, but no migrations history) are first baselined: <see cref="BaselineMigrationId"/>
    /// is recorded as applied so only later migrations run. Non-relational providers
    /// (e.g. InMemory in tests) fall back to <c>EnsureCreated</c>.
    /// </summary>
    public static async Task MigrateWithBaselineAsync(
        this DatabaseFacade database, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (!database.IsRelational())
        {
            await database.EnsureCreatedAsync(cancellationToken);
            return;
        }

        var databaseCreator = database.GetService<IRelationalDatabaseCreator>();
        var historyRepository = database.GetService<IHistoryRepository>();

        if (await databaseCreator.ExistsAsync(cancellationToken)
            && !await historyRepository.ExistsAsync(cancellationToken)
            && await databaseCreator.HasTablesAsync(cancellationToken))
        {
            logger.LogWarning(
                "Database has tables but no migrations history (created via EnsureCreated); recording baseline {Migration}",
                BaselineMigrationId);

            await database.ExecuteSqlRawAsync(historyRepository.GetCreateIfNotExistsScript(), cancellationToken);
            await database.ExecuteSqlRawAsync(
                historyRepository.GetInsertScript(new HistoryRow(BaselineMigrationId, ProductInfo.GetVersion())),
                cancellationToken);
        }

        await database.MigrateAsync(cancellationToken);
    }
}
