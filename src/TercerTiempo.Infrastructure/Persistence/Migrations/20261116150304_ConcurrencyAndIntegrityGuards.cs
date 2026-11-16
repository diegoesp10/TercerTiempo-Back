using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConcurrencyAndIntegrityGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "Version",
                table: "TrialPlayers",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "Version",
                table: "MatchPassRequests",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "Version",
                table: "MatchCallUps",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "pass.no_sheet", "es", "La plantilla aún no ha rellenado el acta interna de este partido: el pase no puede incluirla." },
                    { "sheet.duplicate_player", "es", "Un jugador aparece dos veces en el acta: deja una sola línea por jugador." }
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatchVideos_SquadId_MatchId_Part",
                table: "MatchVideos",
                columns: new[] { "SquadId", "MatchId", "Part" },
                unique: true,
                filter: "[DeletedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MatchPasses_RequestId",
                table: "MatchPasses",
                column: "RequestId",
                unique: true,
                filter: "[RequestId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MatchVideos_SquadId_MatchId_Part",
                table: "MatchVideos");

            migrationBuilder.DropIndex(
                name: "IX_MatchPasses_RequestId",
                table: "MatchPasses");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "pass.no_sheet", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "sheet.duplicate_player", "es" });

            migrationBuilder.DropColumn(
                name: "Version",
                table: "TrialPlayers");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "MatchPassRequests");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "MatchCallUps");
        }
    }
}
