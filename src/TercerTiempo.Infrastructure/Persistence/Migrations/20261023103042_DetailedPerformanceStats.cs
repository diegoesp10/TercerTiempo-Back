using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DetailedPerformanceStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BallLosses",
                table: "PlayerMatchPerformances",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PassesAttempted",
                table: "PlayerMatchPerformances",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PassesCompleted",
                table: "PlayerMatchPerformances",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Recoveries",
                table: "PlayerMatchPerformances",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[] { "sheet.passes_completed", "es", "Los pases completados no pueden superar los pases intentados." });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "sheet.passes_completed", "es" });

            migrationBuilder.DropColumn(
                name: "BallLosses",
                table: "PlayerMatchPerformances");

            migrationBuilder.DropColumn(
                name: "PassesAttempted",
                table: "PlayerMatchPerformances");

            migrationBuilder.DropColumn(
                name: "PassesCompleted",
                table: "PlayerMatchPerformances");

            migrationBuilder.DropColumn(
                name: "Recoveries",
                table: "PlayerMatchPerformances");
        }
    }
}
