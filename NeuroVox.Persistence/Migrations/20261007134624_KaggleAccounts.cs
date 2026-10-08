using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeuroVox.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KaggleAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KaggleAccounts",
                schema: "neurovox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "text", nullable: false),
                    EncryptedApiKey = table.Column<string>(type: "text", nullable: false),
                    EncryptedAiKey = table.Column<string>(type: "text", nullable: true),
                    RegisterTokenHash = table.Column<string>(type: "text", nullable: true),
                    PublicUrl = table.Column<string>(type: "text", nullable: true),
                    LastHeartbeatUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastConnectAttemptUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    RowCreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RowUpdatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RowIsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RowIsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KaggleAccounts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KaggleAccounts_CustomerId_Username",
                schema: "neurovox",
                table: "KaggleAccounts",
                columns: new[] { "CustomerId", "Username" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KaggleAccounts_RegisterTokenHash",
                schema: "neurovox",
                table: "KaggleAccounts",
                column: "RegisterTokenHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KaggleAccounts",
                schema: "neurovox");
        }
    }
}
