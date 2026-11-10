using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AudioTranscription.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPostprocessingTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TemplateId",
                table: "TranscriptVariants",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PostprocessingTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Prompt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OutputSchema = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostprocessingTemplates", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "PostprocessingTemplates",
                columns: new[] { "Id", "Description", "IsBuiltIn", "Name", "OutputSchema", "Prompt" },
                values: new object[,]
                {
                    { new Guid("5a1b0c10-0000-4000-8000-000000000001"), "Zusammenfassung, Beschlüsse und Aufgaben einer Besprechung", true, "Besprechungsprotokoll", "{\"type\":\"object\",\"required\":[\"summary\"],\"properties\":{\n  \"title\":{\"type\":\"string\"},\n  \"summary\":{\"type\":\"string\",\"title\":\"Zusammenfassung\"},\n  \"decisions\":{\"type\":\"array\",\"title\":\"Beschlüsse\",\"items\":{\"type\":\"string\"}},\n  \"tasks\":{\"type\":\"array\",\"title\":\"Aufgaben\",\"items\":{\"type\":\"object\",\"required\":[\"task\"],\"properties\":{\n    \"task\":{\"type\":\"string\"},\"owner\":{\"type\":\"string\",\"title\":\"Verantwortlich\"},\"due\":{\"type\":\"string\",\"title\":\"Fällig\"}}}}}}", "Write minutes of the meeting: a short title, a summary of two to four sentences, the decisions that were made, and the tasks that were assigned, each with its owner and due date if they are mentioned. Use only what is said in the transcript; do not invent facts, names or dates. Leave out a field when the transcript does not say anything about it. Write in {{LANGUAGE}}. Speakers named in the transcript: {{SPEAKERS}}. Name a person as owner only if they are one of these speakers." },
                    { new Guid("5a1b0c10-0000-4000-8000-000000000002"), "Alle Beschlüsse mit Begründung", true, "Entscheidungsliste", "{\"type\":\"object\",\"required\":[\"decisions\"],\"properties\":{\n  \"decisions\":{\"type\":\"array\",\"title\":\"Beschlüsse\",\"items\":{\"type\":\"object\",\"required\":[\"decision\"],\"properties\":{\n    \"decision\":{\"type\":\"string\"},\"reason\":{\"type\":\"string\",\"title\":\"Begründung\"}}}}}}", "List every decision that was made in the conversation, each with a short reason if one is given. Use only what is said in the transcript; do not invent facts, names or dates. Leave out a field when the transcript does not say anything about it. Write in {{LANGUAGE}}. Speakers named in the transcript: {{SPEAKERS}}. Name a person as owner only if they are one of these speakers." },
                    { new Guid("5a1b0c10-0000-4000-8000-000000000003"), "Aufgabenliste mit Verantwortlichen und Terminen", true, "Aufgaben mit Verantwortlichen", "{\"type\":\"object\",\"required\":[\"tasks\"],\"properties\":{\n  \"tasks\":{\"type\":\"array\",\"title\":\"Aufgaben\",\"items\":{\"type\":\"object\",\"required\":[\"task\"],\"properties\":{\n    \"task\":{\"type\":\"string\"},\"owner\":{\"type\":\"string\",\"title\":\"Verantwortlich\"},\"due\":{\"type\":\"string\",\"title\":\"Fällig\"}}}}}}", "List every task that was assigned or agreed on, with the person responsible and the due date if they are mentioned. Use only what is said in the transcript; do not invent facts, names or dates. Leave out a field when the transcript does not say anything about it. Write in {{LANGUAGE}}. Speakers named in the transcript: {{SPEAKERS}}. Name a person as owner only if they are one of these speakers." }
                });

            migrationBuilder.CreateIndex(
                name: "IX_TranscriptVariants_TemplateId",
                table: "TranscriptVariants",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_PostprocessingTemplates_Name",
                table: "PostprocessingTemplates",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_TranscriptVariants_PostprocessingTemplates_TemplateId",
                table: "TranscriptVariants",
                column: "TemplateId",
                principalTable: "PostprocessingTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TranscriptVariants_PostprocessingTemplates_TemplateId",
                table: "TranscriptVariants");

            migrationBuilder.DropTable(
                name: "PostprocessingTemplates");

            migrationBuilder.DropIndex(
                name: "IX_TranscriptVariants_TemplateId",
                table: "TranscriptVariants");

            migrationBuilder.DropColumn(
                name: "TemplateId",
                table: "TranscriptVariants");
        }
    }
}
