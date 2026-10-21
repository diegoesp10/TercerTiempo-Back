using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TeamProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CrestKey",
                table: "Teams",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProfileSyncedAt",
                table: "Teams",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Profile_Address",
                table: "Teams",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Profile_Ground",
                table: "Teams",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Profile_GroundAddress",
                table: "Teams",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Profile_GroundLocality",
                table: "Teams",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Profile_GroundSurface",
                table: "Teams",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Profile_Locality",
                table: "Teams",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Profile_OfficialName",
                table: "Teams",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Profile_PostalCode",
                table: "Teams",
                type: "nvarchar(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Profile_Province",
                table: "Teams",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Profile_ShirtColor",
                table: "Teams",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Profile_ShortsColor",
                table: "Teams",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Profile_SocksColor",
                table: "Teams",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Profile_Website",
                table: "Teams",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CrestKey",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "ProfileSyncedAt",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "Profile_Address",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "Profile_Ground",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "Profile_GroundAddress",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "Profile_GroundLocality",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "Profile_GroundSurface",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "Profile_Locality",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "Profile_OfficialName",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "Profile_PostalCode",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "Profile_Province",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "Profile_ShirtColor",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "Profile_ShortsColor",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "Profile_SocksColor",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "Profile_Website",
                table: "Teams");
        }
    }
}
