using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudioTranscription.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UploadSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OwnerId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RejectionStatus = table.Column<int>(type: "int", nullable: true),
                    RejectionDetail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UploadSessions_CreatedAtUtc",
                table: "UploadSessions",
                column: "CreatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UploadSessions");
        }
    }
}
