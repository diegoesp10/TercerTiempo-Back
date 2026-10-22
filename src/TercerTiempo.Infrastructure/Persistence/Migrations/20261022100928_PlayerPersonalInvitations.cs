using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlayerPersonalInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Squads_InviteCode",
                table: "Squads");

            migrationBuilder.DropIndex(
                name: "IX_SquadPlayers_PlayerId",
                table: "SquadPlayers");

            migrationBuilder.DropColumn(
                name: "InviteCode",
                table: "Squads");

            migrationBuilder.AddColumn<string>(
                name: "InviteCode",
                table: "SquadPlayers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "invite_code.not_found", "es" },
                column: "Message",
                value: "El código de invitación no es válido o ya se ha utilizado. Pide un código personal a tu club o entrenador.");

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "player.invite_changed", "es", "La invitación ha cambiado o ya se ha utilizado. Pide un código nuevo a tu club o entrenador." },
                    { "player.invite_name_mismatch", "es", "El nombre y apellidos de tu cuenta deben coincidir con los de la ficha del jugador. Contacta con tu club o entrenador para revisarlos." }
                });

            migrationBuilder.CreateIndex(
                name: "IX_SquadPlayers_InviteCode",
                table: "SquadPlayers",
                column: "InviteCode",
                unique: true,
                filter: "[InviteCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SquadPlayers_PlayerId",
                table: "SquadPlayers",
                column: "PlayerId",
                unique: true,
                filter: "[PlayerId] IS NOT NULL AND [Status] = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SquadPlayers_InviteCode",
                table: "SquadPlayers");

            migrationBuilder.DropIndex(
                name: "IX_SquadPlayers_PlayerId",
                table: "SquadPlayers");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "player.invite_changed", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "player.invite_name_mismatch", "es" });

            migrationBuilder.DropColumn(
                name: "InviteCode",
                table: "SquadPlayers");

            migrationBuilder.AddColumn<string>(
                name: "InviteCode",
                table: "Squads",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "invite_code.not_found", "es" },
                column: "Message",
                value: "El código de invitación '{0}' no existe.");

            // Al revertir, cada plantilla necesita un valor distinto para recuperar el índice único.
            migrationBuilder.Sql("UPDATE [Squads] SET [InviteCode] = LEFT(REPLACE(CONVERT(varchar(36), NEWID()), '-', ''), 20)");

            migrationBuilder.CreateIndex(
                name: "IX_Squads_InviteCode",
                table: "Squads",
                column: "InviteCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SquadPlayers_PlayerId",
                table: "SquadPlayers",
                column: "PlayerId");
        }
    }
}
