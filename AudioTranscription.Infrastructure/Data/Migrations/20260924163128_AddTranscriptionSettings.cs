using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudioTranscription.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTranscriptionSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "AudioJobs",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Base");

            migrationBuilder.AddColumn<string>(
                name: "RequestedLanguage",
                table: "AudioJobs",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Model",
                table: "AudioJobs");

            migrationBuilder.DropColumn(
                name: "RequestedLanguage",
                table: "AudioJobs");
        }
    }
}
