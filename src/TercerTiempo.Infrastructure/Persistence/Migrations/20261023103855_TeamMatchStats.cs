using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TeamMatchStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TeamMatchStats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SquadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PossessionPct = table.Column<int>(type: "int", nullable: true),
                    CornersFor = table.Column<int>(type: "int", nullable: false),
                    CornersAgainst = table.Column<int>(type: "int", nullable: false),
                    ShotsFor = table.Column<int>(type: "int", nullable: false),
                    ShotsAgainst = table.Column<int>(type: "int", nullable: false),
                    ShotsOnTargetFor = table.Column<int>(type: "int", nullable: false),
                    ShotsOnTargetAgainst = table.Column<int>(type: "int", nullable: false),
                    OffsidesFor = table.Column<int>(type: "int", nullable: false),
                    OffsidesAgainst = table.Column<int>(type: "int", nullable: false),
                    FoulsCommitted = table.Column<int>(type: "int", nullable: false),
                    FoulsReceived = table.Column<int>(type: "int", nullable: false),
                    SetPieceGoalsFor = table.Column<int>(type: "int", nullable: false),
                    SetPieceGoalsAgainst = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RecordedByCoachId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamMatchStats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamMatchStats_Coaches_RecordedByCoachId",
                        column: x => x.RecordedByCoachId,
                        principalTable: "Coaches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamMatchStats_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TeamMatchStats_Squads_SquadId",
                        column: x => x.SquadId,
                        principalTable: "Squads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[] { "team_stats.set_piece_goals", "es", "Los goles a balón parado no pueden superar los goles del partido." });

            migrationBuilder.CreateIndex(
                name: "IX_TeamMatchStats_MatchId",
                table: "TeamMatchStats",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMatchStats_RecordedByCoachId",
                table: "TeamMatchStats",
                column: "RecordedByCoachId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMatchStats_SquadId_MatchId",
                table: "TeamMatchStats",
                columns: new[] { "SquadId", "MatchId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TeamMatchStats");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "team_stats.set_piece_goals", "es" });
        }
    }
}
