using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TercerTiempo.Infrastructure.Persistence.RealDataMigrations
{
    /// <inheritdoc />
    public partial class InjuryProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateTable(
                name: "MatchReportChecks",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    FoundAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Done = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchReportChecks", x => x.MatchId);
                    table.ForeignKey(
                        name: "FK_MatchReportChecks_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "MatchInjuryMentions");

            migrationBuilder.DropTable(
                name: "MatchReportChecks");
        }
    }
}
