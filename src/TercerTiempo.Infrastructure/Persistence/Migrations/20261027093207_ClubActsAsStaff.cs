using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClubActsAsStaff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "squad.staff_only", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "suggestion.no_staff_to_record", "es" });

            migrationBuilder.AlterColumn<Guid>(
                name: "RecordedByCoachId",
                table: "TeamMatchStats",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "RecordedByClubId",
                table: "TeamMatchStats",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "RecordedByCoachId",
                table: "PlayerMatchPerformances",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "RecordedByClubId",
                table: "PlayerMatchPerformances",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "RecordedByCoachId",
                table: "PlayerInjuries",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "RecordedByClubId",
                table: "PlayerInjuries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "sheet.staff_only", "es" },
                column: "Message",
                value: "Solo el club o el cuerpo técnico vigente de la plantilla pueden registrar el acta.");

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[] { "video.match_not_played", "es", "Solo se puede subir el vídeo de un partido ya jugado, desde el acta interna de tu equipo." });

            migrationBuilder.CreateIndex(
                name: "IX_TeamMatchStats_RecordedByClubId",
                table: "TeamMatchStats",
                column: "RecordedByClubId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMatchPerformances_RecordedByClubId",
                table: "PlayerMatchPerformances",
                column: "RecordedByClubId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerInjuries_RecordedByClubId",
                table: "PlayerInjuries",
                column: "RecordedByClubId");

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerInjuries_Clubs_RecordedByClubId",
                table: "PlayerInjuries",
                column: "RecordedByClubId",
                principalTable: "Clubs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerMatchPerformances_Clubs_RecordedByClubId",
                table: "PlayerMatchPerformances",
                column: "RecordedByClubId",
                principalTable: "Clubs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamMatchStats_Clubs_RecordedByClubId",
                table: "TeamMatchStats",
                column: "RecordedByClubId",
                principalTable: "Clubs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlayerInjuries_Clubs_RecordedByClubId",
                table: "PlayerInjuries");

            migrationBuilder.DropForeignKey(
                name: "FK_PlayerMatchPerformances_Clubs_RecordedByClubId",
                table: "PlayerMatchPerformances");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamMatchStats_Clubs_RecordedByClubId",
                table: "TeamMatchStats");

            migrationBuilder.DropIndex(
                name: "IX_TeamMatchStats_RecordedByClubId",
                table: "TeamMatchStats");

            migrationBuilder.DropIndex(
                name: "IX_PlayerMatchPerformances_RecordedByClubId",
                table: "PlayerMatchPerformances");

            migrationBuilder.DropIndex(
                name: "IX_PlayerInjuries_RecordedByClubId",
                table: "PlayerInjuries");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video.match_not_played", "es" });

            migrationBuilder.DropColumn(
                name: "RecordedByClubId",
                table: "TeamMatchStats");

            migrationBuilder.DropColumn(
                name: "RecordedByClubId",
                table: "PlayerMatchPerformances");

            migrationBuilder.DropColumn(
                name: "RecordedByClubId",
                table: "PlayerInjuries");

            migrationBuilder.AlterColumn<Guid>(
                name: "RecordedByCoachId",
                table: "TeamMatchStats",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "RecordedByCoachId",
                table: "PlayerMatchPerformances",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "RecordedByCoachId",
                table: "PlayerInjuries",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "sheet.staff_only", "es" },
                column: "Message",
                value: "Solo el cuerpo técnico vigente de la plantilla puede registrar el acta.");

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "squad.staff_only", "es", "Solo el cuerpo técnico vigente de la plantilla puede hacer esto." },
                    { "suggestion.no_staff_to_record", "es", "No hay ningún técnico vigente en la plantilla en cuyo nombre registrar el acta." }
                });
        }
    }
}
