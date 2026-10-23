using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeuroVox.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AphasiaRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AphasiaRatings",
                schema: "neurovox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordingId = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnnotatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RaterIndex = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RowCreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RowUpdatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RowIsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RowIsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AphasiaRatings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AphasiaRatings_RecordingId_AnnotatorId",
                schema: "neurovox",
                table: "AphasiaRatings",
                columns: new[] { "RecordingId", "AnnotatorId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AphasiaRatings",
                schema: "neurovox");
        }
    }
}
