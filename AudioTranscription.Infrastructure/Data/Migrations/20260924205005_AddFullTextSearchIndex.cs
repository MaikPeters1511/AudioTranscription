using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudioTranscription.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Full-text search (S13, ADR 0005): SQL Server Full-Text Search over the three places a
    /// transcript's words live. Not an EF Core model concept, so it is raw SQL. German (1031) is used
    /// for stemming, since transcription targets German recordings first (see ADR 0005).
    /// </summary>
    /// <remarks>
    /// Full-Text Search is a separate component the stock <c>mcr.microsoft.com/mssql/server</c> image
    /// does not include (see ADR 0005); Aspire and docker-compose build <c>docker/mssql-fts</c> instead,
    /// which does. Every statement here is guarded by <c>IsFullTextInstalled</c> so this migration still
    /// succeeds as a no-op against a database whose server does not have it (e.g. this repo's other,
    /// non-search integration tests, or an operator who has not yet switched to the custom image);
    /// <c>/api/search</c> itself then fails until the operator does switch, the same way S10/S11's
    /// optional features fail closed without their own configuration.
    /// </remarks>
    public partial class AddFullTextSearchIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CREATE FULLTEXT CATALOG/INDEX cannot run inside a transaction (SQL Server restriction)
            migrationBuilder.Sql("""
                IF SERVERPROPERTY('IsFullTextInstalled') = 1
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'SearchCatalog')
                        EXEC('CREATE FULLTEXT CATALOG [SearchCatalog] AS DEFAULT');
                END
                """, suppressTransaction: true);

            migrationBuilder.Sql("""
                IF SERVERPROPERTY('IsFullTextInstalled') = 1 AND NOT EXISTS
                    (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('AudioJobs'))
                    EXEC('CREATE FULLTEXT INDEX ON [AudioJobs] ([RawTranscript] LANGUAGE 1031)
                        KEY INDEX [PK_AudioJobs] ON [SearchCatalog] WITH CHANGE_TRACKING AUTO');
                """, suppressTransaction: true);

            migrationBuilder.Sql("""
                IF SERVERPROPERTY('IsFullTextInstalled') = 1 AND NOT EXISTS
                    (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('TranscriptSegments'))
                    EXEC('CREATE FULLTEXT INDEX ON [TranscriptSegments] ([Text] LANGUAGE 1031)
                        KEY INDEX [PK_TranscriptSegments] ON [SearchCatalog] WITH CHANGE_TRACKING AUTO');
                """, suppressTransaction: true);

            migrationBuilder.Sql("""
                IF SERVERPROPERTY('IsFullTextInstalled') = 1 AND NOT EXISTS
                    (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('TranscriptVariants'))
                    EXEC('CREATE FULLTEXT INDEX ON [TranscriptVariants] ([Text] LANGUAGE 1031)
                        KEY INDEX [PK_TranscriptVariants] ON [SearchCatalog] WITH CHANGE_TRACKING AUTO');
                """, suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('TranscriptVariants'))
                    DROP FULLTEXT INDEX ON [TranscriptVariants];
                """, suppressTransaction: true);
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('TranscriptSegments'))
                    DROP FULLTEXT INDEX ON [TranscriptSegments];
                """, suppressTransaction: true);
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('AudioJobs'))
                    DROP FULLTEXT INDEX ON [AudioJobs];
                """, suppressTransaction: true);
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'SearchCatalog')
                    DROP FULLTEXT CATALOG [SearchCatalog];
                """, suppressTransaction: true);
        }
    }
}
