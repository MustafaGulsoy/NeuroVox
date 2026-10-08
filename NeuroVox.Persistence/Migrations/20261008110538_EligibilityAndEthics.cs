using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeuroVox.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EligibilityAndEthics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "EthicsApprovalDate",
                schema: "neurovox",
                table: "ResearchProtocols",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EthicsApprovalNumber",
                schema: "neurovox",
                table: "ResearchProtocols",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EthicsCommittee",
                schema: "neurovox",
                table: "ResearchProtocols",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AdequateVisionHearing",
                schema: "neurovox",
                table: "Participants",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Diagnosis",
                schema: "neurovox",
                table: "Participants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EligibilityAssessedAt",
                schema: "neurovox",
                table: "Participants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EligibilityAssessedBy",
                schema: "neurovox",
                table: "Participants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LanguageBarrier",
                schema: "neurovox",
                table: "Participants",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SevereMentalIllness",
                schema: "neurovox",
                table: "Participants",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SevereNeurologicalDeficit",
                schema: "neurovox",
                table: "Participants",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WillingFollowUp6Months",
                schema: "neurovox",
                table: "Participants",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EthicsApprovalDate",
                schema: "neurovox",
                table: "ResearchProtocols");

            migrationBuilder.DropColumn(
                name: "EthicsApprovalNumber",
                schema: "neurovox",
                table: "ResearchProtocols");

            migrationBuilder.DropColumn(
                name: "EthicsCommittee",
                schema: "neurovox",
                table: "ResearchProtocols");

            migrationBuilder.DropColumn(
                name: "AdequateVisionHearing",
                schema: "neurovox",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "Diagnosis",
                schema: "neurovox",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "EligibilityAssessedAt",
                schema: "neurovox",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "EligibilityAssessedBy",
                schema: "neurovox",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "LanguageBarrier",
                schema: "neurovox",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "SevereMentalIllness",
                schema: "neurovox",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "SevereNeurologicalDeficit",
                schema: "neurovox",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "WillingFollowUp6Months",
                schema: "neurovox",
                table: "Participants");
        }
    }
}
