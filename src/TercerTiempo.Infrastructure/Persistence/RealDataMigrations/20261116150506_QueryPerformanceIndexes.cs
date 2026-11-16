using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TercerTiempo.Infrastructure.Persistence.RealDataMigrations
{
    /// <inheritdoc />
    public partial class QueryPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Matches_AwayTeamId",
                table: "Matches");

            migrationBuilder.DropIndex(
                name: "IX_Matches_HomeTeamId",
                table: "Matches");

            migrationBuilder.CreateIndex(
                name: "IX_OfficialSheets_Provider_Status",
                table: "OfficialSheets",
                columns: new[] { "Provider", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_AwayTeamId_KickoffAt",
                table: "Matches",
                columns: new[] { "AwayTeamId", "KickoffAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_HomeTeamId_KickoffAt",
                table: "Matches",
                columns: new[] { "HomeTeamId", "KickoffAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_Status_KickoffAt",
                table: "Matches",
                columns: new[] { "Status", "KickoffAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OfficialSheets_Provider_Status",
                table: "OfficialSheets");

            migrationBuilder.DropIndex(
                name: "IX_Matches_AwayTeamId_KickoffAt",
                table: "Matches");

            migrationBuilder.DropIndex(
                name: "IX_Matches_HomeTeamId_KickoffAt",
                table: "Matches");

            migrationBuilder.DropIndex(
                name: "IX_Matches_Status_KickoffAt",
                table: "Matches");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_AwayTeamId",
                table: "Matches",
                column: "AwayTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_HomeTeamId",
                table: "Matches",
                column: "HomeTeamId");
        }
    }
}
