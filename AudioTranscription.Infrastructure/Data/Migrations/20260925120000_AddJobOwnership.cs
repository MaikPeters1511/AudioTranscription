using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudioTranscription.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJobOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "AudioJobs",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "JobShares",
                columns: table => new
                {
                    AudioJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    SharedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobShares", x => new { x.AudioJobId, x.UserId });
                    table.ForeignKey(
                        name: "FK_JobShares_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JobShares_AudioJobs_AudioJobId",
                        column: x => x.AudioJobId,
                        principalTable: "AudioJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AudioJobs_OwnerId",
                table: "AudioJobs",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_JobShares_UserId",
                table: "JobShares",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AudioJobs_AspNetUsers_OwnerId",
                table: "AudioJobs",
                column: "OwnerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Existing jobs predate ownership: give them to the first admin. Without an admin they stay
            // unassigned (and invisible) until an admin assigns them: handing transcripts to an arbitrary
            // user would silently expose them (S19, ADR 0007).
            migrationBuilder.Sql(@"
                UPDATE AudioJobs
                SET OwnerId = (SELECT TOP 1 ur.UserId
                                 FROM AspNetUserRoles ur
                                 JOIN AspNetRoles r ON r.Id = ur.RoleId
                                WHERE r.NormalizedName = 'ADMIN'
                                ORDER BY ur.UserId)
                WHERE OwnerId IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AudioJobs_AspNetUsers_OwnerId",
                table: "AudioJobs");

            migrationBuilder.DropTable(
                name: "JobShares");

            migrationBuilder.DropIndex(
                name: "IX_AudioJobs_OwnerId",
                table: "AudioJobs");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "AudioJobs");
        }
    }
}
