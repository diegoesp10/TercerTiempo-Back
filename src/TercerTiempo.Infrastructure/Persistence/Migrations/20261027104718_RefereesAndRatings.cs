using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RefereesAndRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MatchOfficialLookups",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchOfficialLookups", x => x.MatchId);
                    table.ForeignKey(
                        name: "FK_MatchOfficialLookups_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Referees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Referees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MatchOfficials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RefereeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchOfficials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchOfficials_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchOfficials_Referees_RefereeId",
                        column: x => x.RefereeId,
                        principalTable: "Referees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RefereeRatings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RefereeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RatedByClubId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RatedByCoachId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefereeRatings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefereeRatings_Clubs_RatedByClubId",
                        column: x => x.RatedByClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RefereeRatings_Coaches_RatedByCoachId",
                        column: x => x.RatedByCoachId,
                        principalTable: "Coaches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RefereeRatings_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RefereeRatings_Referees_RefereeId",
                        column: x => x.RefereeId,
                        principalTable: "Referees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RefereeRatings_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "referee.match_not_over", "es", "Solo se puede puntuar al equipo arbitral de un partido ya jugado." },
                    { "referee.not_found", "es", "El árbitro '{0}' no existe." },
                    { "referee.not_in_match", "es", "Ese árbitro no figura en el equipo arbitral de este partido." },
                    { "referee.rater_required", "es", "La nota la pone el club o un técnico del equipo." },
                    { "referee.role_invalid", "es", "Función del equipo arbitral no válida." },
                    { "referee.score_range", "es", "La nota va de 0 a {0}." },
                    { "referee.staff_only", "es", "Los árbitros y sus notas son para los clubes y sus cuerpos técnicos, con licencia vigente." },
                    { "referee.team_not_in_match", "es", "Solo pueden puntuar al equipo arbitral los equipos que jugaron el partido: su club o su cuerpo técnico vigente." }
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatchOfficials_MatchId_Role",
                table: "MatchOfficials",
                columns: new[] { "MatchId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchOfficials_RefereeId",
                table: "MatchOfficials",
                column: "RefereeId");

            migrationBuilder.CreateIndex(
                name: "IX_RefereeRatings_MatchId_TeamId_RefereeId",
                table: "RefereeRatings",
                columns: new[] { "MatchId", "TeamId", "RefereeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefereeRatings_RatedByClubId",
                table: "RefereeRatings",
                column: "RatedByClubId");

            migrationBuilder.CreateIndex(
                name: "IX_RefereeRatings_RatedByCoachId",
                table: "RefereeRatings",
                column: "RatedByCoachId");

            migrationBuilder.CreateIndex(
                name: "IX_RefereeRatings_RefereeId",
                table: "RefereeRatings",
                column: "RefereeId");

            migrationBuilder.CreateIndex(
                name: "IX_RefereeRatings_TeamId",
                table: "RefereeRatings",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Referees_NormalizedName",
                table: "Referees",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Referees_Slug",
                table: "Referees",
                column: "Slug");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchOfficialLookups");

            migrationBuilder.DropTable(
                name: "MatchOfficials");

            migrationBuilder.DropTable(
                name: "RefereeRatings");

            migrationBuilder.DropTable(
                name: "Referees");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "referee.match_not_over", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "referee.not_found", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "referee.not_in_match", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "referee.rater_required", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "referee.role_invalid", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "referee.score_range", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "referee.staff_only", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "referee.team_not_in_match", "es" });
        }
    }
}
