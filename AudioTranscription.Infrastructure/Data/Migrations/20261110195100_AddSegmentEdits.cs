using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudioTranscription.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSegmentEdits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrentText",
                table: "TranscriptSegments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "TranscriptSegments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "SegmentEdits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SegmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EditedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    EditedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SegmentEdits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SegmentEdits_TranscriptSegments_SegmentId",
                        column: x => x.SegmentId,
                        principalTable: "TranscriptSegments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SegmentEdits_SegmentId_Version",
                table: "SegmentEdits",
                columns: new[] { "SegmentId", "Version" },
                unique: true);

            // Search (S13) must find corrected text: add the column to the segments' full-text index.
            // Guarded like the original index, so it is a no-op where Full-Text Search is not installed.
            migrationBuilder.Sql("""
                IF SERVERPROPERTY('IsFullTextInstalled') = 1 AND EXISTS
                    (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('TranscriptSegments'))
                    EXEC('ALTER FULLTEXT INDEX ON [TranscriptSegments] ADD ([CurrentText] LANGUAGE 1031)');
                """, suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF SERVERPROPERTY('IsFullTextInstalled') = 1 AND EXISTS
                    (SELECT 1 FROM sys.fulltext_index_columns c
                     WHERE c.object_id = OBJECT_ID('TranscriptSegments') AND c.column_id = COLUMNPROPERTY(OBJECT_ID('TranscriptSegments'), 'CurrentText', 'ColumnId'))
                    EXEC('ALTER FULLTEXT INDEX ON [TranscriptSegments] DROP ([CurrentText])');
                """, suppressTransaction: true);

            migrationBuilder.DropTable(
                name: "SegmentEdits");

            migrationBuilder.DropColumn(
                name: "CurrentText",
                table: "TranscriptSegments");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "TranscriptSegments");
        }
    }
}
