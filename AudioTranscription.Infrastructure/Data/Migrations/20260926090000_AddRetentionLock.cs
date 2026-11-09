using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudioTranscription.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRetentionLock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RetentionLocked",
                table: "AudioJobs",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RetentionLocked",
                table: "AudioJobs");
        }
    }
}
