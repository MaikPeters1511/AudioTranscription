using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudioTranscription.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTranscriptVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TranscriptVariants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AudioJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    TargetLanguage = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TranscriptVariants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TranscriptVariants_AudioJobs_AudioJobId",
                        column: x => x.AudioJobId,
                        principalTable: "AudioJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TranscriptVariants_AudioJobId_Mode_TargetLanguage",
                table: "TranscriptVariants",
                columns: new[] { "AudioJobId", "Mode", "TargetLanguage" });

            // S04's automatic cleanup becomes the Cleanup variant (S10); Mode 0 = Cleanup, Status 1 = Completed
            migrationBuilder.Sql("""
                INSERT INTO [TranscriptVariants] ([Id], [AudioJobId], [Mode], [TargetLanguage], [Status], [Text], [CreatedAtUtc], [CompletedAtUtc])
                SELECT NEWID(), [Id], 0, NULL, 1, [ProcessedTranscript], COALESCE([CompletedAtUtc], [CreatedAtUtc]), [CompletedAtUtc]
                FROM [AudioJobs]
                WHERE [ProcessedTranscript] IS NOT NULL
                """);

            migrationBuilder.DropColumn(
                name: "ProcessedTranscript",
                table: "AudioJobs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TranscriptVariants");

            migrationBuilder.AddColumn<string>(
                name: "ProcessedTranscript",
                table: "AudioJobs",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
