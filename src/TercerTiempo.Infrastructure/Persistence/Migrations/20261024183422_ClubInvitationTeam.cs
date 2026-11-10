using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClubInvitationTeam : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TeamId",
                table: "ClubInvitations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClubInvitations_TeamId",
                table: "ClubInvitations",
                column: "TeamId");

            migrationBuilder.AddForeignKey(
                name: "FK_ClubInvitations_Teams_TeamId",
                table: "ClubInvitations",
                column: "TeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClubInvitations_Teams_TeamId",
                table: "ClubInvitations");

            migrationBuilder.DropIndex(
                name: "IX_ClubInvitations_TeamId",
                table: "ClubInvitations");

            migrationBuilder.DropColumn(
                name: "TeamId",
                table: "ClubInvitations");
        }
    }
}
