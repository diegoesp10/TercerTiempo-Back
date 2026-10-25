using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TeamStatsPenaltiesAndThrowIns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PenaltiesAgainst",
                table: "TeamMatchStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PenaltiesFor",
                table: "TeamMatchStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PenaltyGoalsAgainst",
                table: "TeamMatchStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PenaltyGoalsFor",
                table: "TeamMatchStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ThrowInsAgainst",
                table: "TeamMatchStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ThrowInsFor",
                table: "TeamMatchStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[] { "team_stats.penalty_goals", "es", "Los goles de penalti no pueden superar los penaltis ni los goles a balón parado." });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "team_stats.penalty_goals", "es" });

            migrationBuilder.DropColumn(
                name: "PenaltiesAgainst",
                table: "TeamMatchStats");

            migrationBuilder.DropColumn(
                name: "PenaltiesFor",
                table: "TeamMatchStats");

            migrationBuilder.DropColumn(
                name: "PenaltyGoalsAgainst",
                table: "TeamMatchStats");

            migrationBuilder.DropColumn(
                name: "PenaltyGoalsFor",
                table: "TeamMatchStats");

            migrationBuilder.DropColumn(
                name: "ThrowInsAgainst",
                table: "TeamMatchStats");

            migrationBuilder.DropColumn(
                name: "ThrowInsFor",
                table: "TeamMatchStats");
        }
    }
}
