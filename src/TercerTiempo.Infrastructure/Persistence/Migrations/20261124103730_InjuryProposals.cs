using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InjuryProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InjuryProposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SquadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SquadPlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MentionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Minute = table.Column<int>(type: "int", nullable: true),
                    Excerpt = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    Phrase = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    SourceUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    InjuryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DetectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecidedByCoachId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedByClubId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InjuryProposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InjuryProposals_Clubs_DecidedByClubId",
                        column: x => x.DecidedByClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InjuryProposals_Coaches_DecidedByCoachId",
                        column: x => x.DecidedByCoachId,
                        principalTable: "Coaches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InjuryProposals_SquadPlayers_SquadPlayerId",
                        column: x => x.SquadPlayerId,
                        principalTable: "SquadPlayers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InjuryProposals_Squads_SquadId",
                        column: x => x.SquadId,
                        principalTable: "Squads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchInjuryMentions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Side = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PlayerName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ShirtNumber = table.Column<int>(type: "int", nullable: true),
                    Minute = table.Column<int>(type: "int", nullable: true),
                    Excerpt = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    Phrase = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SourceUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    DetectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchInjuryMentions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchInjuryMentions_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "injury_proposal.decided", "es", "Esa posible lesión ya se registró o se descartó." },
                    { "injury_proposal.mismatch", "es", "La posible lesión es de otro jugador: regístrala desde su aviso." },
                    { "injury_proposal.not_found", "es", "La posible lesión '{0}' no existe." }
                });

            migrationBuilder.CreateIndex(
                name: "IX_InjuryProposals_DecidedByClubId",
                table: "InjuryProposals",
                column: "DecidedByClubId");

            migrationBuilder.CreateIndex(
                name: "IX_InjuryProposals_DecidedByCoachId",
                table: "InjuryProposals",
                column: "DecidedByCoachId");

            migrationBuilder.CreateIndex(
                name: "IX_InjuryProposals_MatchId",
                table: "InjuryProposals",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_InjuryProposals_SquadId_MatchId_SquadPlayerId",
                table: "InjuryProposals",
                columns: new[] { "SquadId", "MatchId", "SquadPlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InjuryProposals_SquadPlayerId",
                table: "InjuryProposals",
                column: "SquadPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchInjuryMentions_MatchId_Side_NormalizedName",
                table: "MatchInjuryMentions",
                columns: new[] { "MatchId", "Side", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchInjuryMentions_UpdatedAt",
                table: "MatchInjuryMentions",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InjuryProposals");

            migrationBuilder.DropTable(
                name: "MatchInjuryMentions");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury_proposal.decided", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury_proposal.mismatch", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury_proposal.not_found", "es" });
        }
    }
}
