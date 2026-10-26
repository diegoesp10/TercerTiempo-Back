using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FederationRequestBursts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BurstRequests",
                table: "FederationRequestStates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastRequestAt",
                table: "FederationRequestStates",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PauseKind",
                table: "FederationRequestStates",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PauseReason",
                table: "FederationRequestStates",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SessionEpoch",
                table: "FederationRequestStates",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BurstRequests",
                table: "FederationRequestStates");

            migrationBuilder.DropColumn(
                name: "LastRequestAt",
                table: "FederationRequestStates");

            migrationBuilder.DropColumn(
                name: "PauseKind",
                table: "FederationRequestStates");

            migrationBuilder.DropColumn(
                name: "PauseReason",
                table: "FederationRequestStates");

            migrationBuilder.DropColumn(
                name: "SessionEpoch",
                table: "FederationRequestStates");
        }
    }
}
