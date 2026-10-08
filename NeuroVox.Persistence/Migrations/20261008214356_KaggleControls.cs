using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeuroVox.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KaggleControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Enabled",
                schema: "neurovox",
                table: "KaggleAccounts",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "KernelVersion",
                schema: "neurovox",
                table: "KaggleAccounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResumeAtUtc",
                schema: "neurovox",
                table: "KaggleAccounts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Enabled",
                schema: "neurovox",
                table: "KaggleAccounts");

            migrationBuilder.DropColumn(
                name: "KernelVersion",
                schema: "neurovox",
                table: "KaggleAccounts");

            migrationBuilder.DropColumn(
                name: "ResumeAtUtc",
                schema: "neurovox",
                table: "KaggleAccounts");
        }
    }
}
