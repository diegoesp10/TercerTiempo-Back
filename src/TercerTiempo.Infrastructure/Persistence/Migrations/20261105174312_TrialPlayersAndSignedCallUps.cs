using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrialPlayersAndSignedCallUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Las convocatorias ya no admiten «no puedo»: lo que había rechazado vuelve a estar sin firmar y los avisos
            // de rechazo que tenía el cuerpo técnico desaparecen (la clase ya no conoce ese tipo de aviso).
            migrationBuilder.Sql("UPDATE [CallUpEntries] SET [Response] = N'Pending', [RespondedAt] = NULL WHERE [Response] = N'Declined';");
            migrationBuilder.Sql("DELETE FROM [Alerts] WHERE [Kind] = N'CallUpDeclined';");

            migrationBuilder.DropIndex(
                name: "IX_TrainingAttendances_SessionId_SquadPlayerId",
                table: "TrainingAttendances");

            migrationBuilder.DropColumn(
                name: "ResponseNote",
                table: "CallUpEntries");

            migrationBuilder.AlterColumn<Guid>(
                name: "SquadPlayerId",
                table: "TrainingAttendances",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "TrialPlayerId",
                table: "TrainingAttendances",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TrialPlayers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SquadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByCoachId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByClubId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PromotedSquadPlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PromotedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrialPlayers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrialPlayers_Clubs_CreatedByClubId",
                        column: x => x.CreatedByClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrialPlayers_Coaches_CreatedByCoachId",
                        column: x => x.CreatedByCoachId,
                        principalTable: "Coaches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrialPlayers_SquadPlayers_PromotedSquadPlayerId",
                        column: x => x.PromotedSquadPlayerId,
                        principalTable: "SquadPlayers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrialPlayers_Squads_SquadId",
                        column: x => x.SquadId,
                        principalTable: "Squads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "callup.invalid_response", "es" },
                column: "Message",
                value: "Respuesta no válida: el convocado firma la convocatoria; el que no va se da por enterado.");

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "training.attendance_target", "es", "Cada línea de asistencia es de un jugador de la plantilla o de un jugador de prueba, no de los dos." },
                    { "trial_player.duplicate", "es", "Ya hay un jugador de prueba llamado '{0}' en esta plantilla." },
                    { "trial_player.not_found", "es", "El jugador de prueba '{0}' no existe en esta plantilla." },
                    { "trial_player.not_in_session", "es", "Ese jugador de prueba no está en este entrenamiento." },
                    { "trial_player.promoted", "es", "{0} ya tiene ficha en la plantilla: ya no es jugador de prueba." }
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingAttendances_SessionId_SquadPlayerId",
                table: "TrainingAttendances",
                columns: new[] { "SessionId", "SquadPlayerId" },
                unique: true,
                filter: "[SquadPlayerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingAttendances_SessionId_TrialPlayerId",
                table: "TrainingAttendances",
                columns: new[] { "SessionId", "TrialPlayerId" },
                unique: true,
                filter: "[TrialPlayerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingAttendances_TrialPlayerId",
                table: "TrainingAttendances",
                column: "TrialPlayerId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TrainingAttendances_OneSubject",
                table: "TrainingAttendances",
                sql: "([SquadPlayerId] IS NOT NULL AND [TrialPlayerId] IS NULL) OR ([SquadPlayerId] IS NULL AND [TrialPlayerId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_TrialPlayers_CreatedByClubId",
                table: "TrialPlayers",
                column: "CreatedByClubId");

            migrationBuilder.CreateIndex(
                name: "IX_TrialPlayers_CreatedByCoachId",
                table: "TrialPlayers",
                column: "CreatedByCoachId");

            migrationBuilder.CreateIndex(
                name: "IX_TrialPlayers_PromotedSquadPlayerId",
                table: "TrialPlayers",
                column: "PromotedSquadPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_TrialPlayers_SquadId",
                table: "TrialPlayers",
                column: "SquadId");

            migrationBuilder.AddForeignKey(
                name: "FK_TrainingAttendances_TrialPlayers_TrialPlayerId",
                table: "TrainingAttendances",
                column: "TrialPlayerId",
                principalTable: "TrialPlayers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TrainingAttendances_TrialPlayers_TrialPlayerId",
                table: "TrainingAttendances");

            migrationBuilder.DropTable(
                name: "TrialPlayers");

            migrationBuilder.DropIndex(
                name: "IX_TrainingAttendances_SessionId_SquadPlayerId",
                table: "TrainingAttendances");

            migrationBuilder.DropIndex(
                name: "IX_TrainingAttendances_SessionId_TrialPlayerId",
                table: "TrainingAttendances");

            migrationBuilder.DropIndex(
                name: "IX_TrainingAttendances_TrialPlayerId",
                table: "TrainingAttendances");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TrainingAttendances_OneSubject",
                table: "TrainingAttendances");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "training.attendance_target", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "trial_player.duplicate", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "trial_player.not_found", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "trial_player.not_in_session", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "trial_player.promoted", "es" });

            migrationBuilder.DropColumn(
                name: "TrialPlayerId",
                table: "TrainingAttendances");

            migrationBuilder.AlterColumn<Guid>(
                name: "SquadPlayerId",
                table: "TrainingAttendances",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseNote",
                table: "CallUpEntries",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "callup.invalid_response", "es" },
                column: "Message",
                value: "Respuesta no válida: el convocado confirma o avisa de que no puede; el que no va se da por enterado.");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingAttendances_SessionId_SquadPlayerId",
                table: "TrainingAttendances",
                columns: new[] { "SessionId", "SquadPlayerId" },
                unique: true);
        }
    }
}
