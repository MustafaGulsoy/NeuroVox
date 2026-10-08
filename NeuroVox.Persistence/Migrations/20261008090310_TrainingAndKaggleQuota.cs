using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeuroVox.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrainingAndKaggleQuota : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "GpuExhaustedUntilUtc",
                schema: "neurovox",
                table: "KaggleAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "GpuSecondsThisWeek",
                schema: "neurovox",
                table: "KaggleAccounts",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<DateTime>(
                name: "KernelStartedUtc",
                schema: "neurovox",
                table: "KaggleAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QuotaWeekStartUtc",
                schema: "neurovox",
                table: "KaggleAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RunningOnGpu",
                schema: "neurovox",
                table: "KaggleAccounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "TrainingRuns",
                schema: "neurovox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true),
                    SampleCount = table.Column<int>(type: "integer", nullable: false),
                    ReportJson = table.Column<string>(type: "text", nullable: true),
                    ArtifactPath = table.Column<string>(type: "text", nullable: true),
                    RequestedBy = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RowCreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RowUpdatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RowIsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RowIsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingRuns", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrainingRuns",
                schema: "neurovox");

            migrationBuilder.DropColumn(
                name: "GpuExhaustedUntilUtc",
                schema: "neurovox",
                table: "KaggleAccounts");

            migrationBuilder.DropColumn(
                name: "GpuSecondsThisWeek",
                schema: "neurovox",
                table: "KaggleAccounts");

            migrationBuilder.DropColumn(
                name: "KernelStartedUtc",
                schema: "neurovox",
                table: "KaggleAccounts");

            migrationBuilder.DropColumn(
                name: "QuotaWeekStartUtc",
                schema: "neurovox",
                table: "KaggleAccounts");

            migrationBuilder.DropColumn(
                name: "RunningOnGpu",
                schema: "neurovox",
                table: "KaggleAccounts");
        }
    }
}
