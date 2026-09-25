using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudioTranscription.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSpeakerDiarization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SpeakerIndex",
                table: "TranscriptSegments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DiarizationRequested",
                table: "AudioJobs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "JobSpeakers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AudioJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Index = table.Column<int>(type: "int", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobSpeakers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobSpeakers_AudioJobs_AudioJobId",
                        column: x => x.AudioJobId,
                        principalTable: "AudioJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobSpeakers_AudioJobId_Index",
                table: "JobSpeakers",
                columns: new[] { "AudioJobId", "Index" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobSpeakers");

            migrationBuilder.DropColumn(
                name: "SpeakerIndex",
                table: "TranscriptSegments");

            migrationBuilder.DropColumn(
                name: "DiarizationRequested",
                table: "AudioJobs");
        }
    }
}
