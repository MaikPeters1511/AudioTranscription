using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudioTranscription.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SplitRawAndProcessedTranscript : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TranscriptText",
                table: "AudioJobs",
                newName: "RawTranscript");

            migrationBuilder.AddColumn<string>(
                name: "ProcessedTranscript",
                table: "AudioJobs",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProcessedTranscript",
                table: "AudioJobs");

            migrationBuilder.RenameColumn(
                name: "RawTranscript",
                table: "AudioJobs",
                newName: "TranscriptText");
        }
    }
}
