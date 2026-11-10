using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PerformanceSuggestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PerformanceSuggestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SquadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SquadPlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Started = table.Column<bool>(type: "bit", nullable: false),
                    MinutesPlayed = table.Column<int>(type: "int", nullable: false),
                    Goals = table.Column<int>(type: "int", nullable: false),
                    Assists = table.Column<int>(type: "int", nullable: false),
                    Shots = table.Column<int>(type: "int", nullable: false),
                    ShotsOnTarget = table.Column<int>(type: "int", nullable: false),
                    KeyPasses = table.Column<int>(type: "int", nullable: false),
                    Tackles = table.Column<int>(type: "int", nullable: false),
                    Interceptions = table.Column<int>(type: "int", nullable: false),
                    Saves = table.Column<int>(type: "int", nullable: false),
                    YellowCards = table.Column<int>(type: "int", nullable: false),
                    RedCard = table.Column<bool>(type: "bit", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResolvedByCoachId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolvedByClubId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolutionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerformanceSuggestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PerformanceSuggestions_Clubs_ResolvedByClubId",
                        column: x => x.ResolvedByClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PerformanceSuggestions_Coaches_ResolvedByCoachId",
                        column: x => x.ResolvedByCoachId,
                        principalTable: "Coaches",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PerformanceSuggestions_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PerformanceSuggestions_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PerformanceSuggestions_SquadPlayers_SquadPlayerId",
                        column: x => x.SquadPlayerId,
                        principalTable: "SquadPlayers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PerformanceSuggestions_Squads_SquadId",
                        column: x => x.SquadId,
                        principalTable: "Squads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "suggestion.already_resolved", "es", "Esa sugerencia ya se ha aprobado o rechazado." },
                    { "suggestion.message_too_long", "es", "El mensaje no puede superar los {0} caracteres." },
                    { "suggestion.no_staff_to_record", "es", "No hay ningún técnico vigente en la plantilla en cuyo nombre registrar el acta." },
                    { "suggestion.not_found", "es", "La sugerencia '{0}' no existe." },
                    { "suggestion.pending_exists", "es", "Ya tienes una sugerencia pendiente para este partido. Espera a que tu club o tu entrenador la revisen." }
                });

            migrationBuilder.CreateIndex(
                name: "IX_PerformanceSuggestions_MatchId",
                table: "PerformanceSuggestions",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PerformanceSuggestions_PlayerId",
                table: "PerformanceSuggestions",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PerformanceSuggestions_ResolvedByClubId",
                table: "PerformanceSuggestions",
                column: "ResolvedByClubId");

            migrationBuilder.CreateIndex(
                name: "IX_PerformanceSuggestions_ResolvedByCoachId",
                table: "PerformanceSuggestions",
                column: "ResolvedByCoachId");

            migrationBuilder.CreateIndex(
                name: "IX_PerformanceSuggestions_SquadId_Status",
                table: "PerformanceSuggestions",
                columns: new[] { "SquadId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PerformanceSuggestions_SquadPlayerId",
                table: "PerformanceSuggestions",
                column: "SquadPlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PerformanceSuggestions");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "suggestion.already_resolved", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "suggestion.message_too_long", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "suggestion.no_staff_to_record", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "suggestion.not_found", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "suggestion.pending_exists", "es" });
        }
    }
}
