using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeuroVox.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConsentAndAnalysisStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnalysisError",
                schema: "neurovox",
                table: "SpeechRecordings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AnalysisStatus",
                schema: "neurovox",
                table: "SpeechRecordings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "AnalyzedAt",
                schema: "neurovox",
                table: "SpeechRecordings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConsentGivenAt",
                schema: "neurovox",
                table: "Participants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConsentVersion",
                schema: "neurovox",
                table: "Participants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConsentWithdrawnAt",
                schema: "neurovox",
                table: "Participants",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnalysisError",
                schema: "neurovox",
                table: "SpeechRecordings");

            migrationBuilder.DropColumn(
                name: "AnalysisStatus",
                schema: "neurovox",
                table: "SpeechRecordings");

            migrationBuilder.DropColumn(
                name: "AnalyzedAt",
                schema: "neurovox",
                table: "SpeechRecordings");

            migrationBuilder.DropColumn(
                name: "ConsentGivenAt",
                schema: "neurovox",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "ConsentVersion",
                schema: "neurovox",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "ConsentWithdrawnAt",
                schema: "neurovox",
                table: "Participants");
        }
    }
}
