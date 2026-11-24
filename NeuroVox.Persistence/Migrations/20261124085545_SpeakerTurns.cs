using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeuroVox.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SpeakerTurns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExpectedSpeakers",
                schema: "neurovox",
                table: "SpeechRecordings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpeakerTurnsJson",
                schema: "neurovox",
                table: "SpeechRecordings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpectedSpeakers",
                schema: "neurovox",
                table: "SpeechRecordings");

            migrationBuilder.DropColumn(
                name: "SpeakerTurnsJson",
                schema: "neurovox",
                table: "SpeechRecordings");
        }
    }
}
