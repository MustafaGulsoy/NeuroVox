using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeuroVox.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DoctorSegments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DoctorSegmentsJson",
                schema: "neurovox",
                table: "SpeechRecordings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DoctorSegmentsSource",
                schema: "neurovox",
                table: "SpeechRecordings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DoctorSegmentsJson",
                schema: "neurovox",
                table: "SpeechRecordings");

            migrationBuilder.DropColumn(
                name: "DoctorSegmentsSource",
                schema: "neurovox",
                table: "SpeechRecordings");
        }
    }
}
