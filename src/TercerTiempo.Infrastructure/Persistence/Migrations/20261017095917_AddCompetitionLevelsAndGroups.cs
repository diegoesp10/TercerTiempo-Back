using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetitionLevelsAndGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Competitions",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Group",
                table: "Competitions",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Level",
                table: "Competitions",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Other");

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Competitions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Tier",
                table: "Competitions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Competitions_SortOrder",
                table: "Competitions",
                column: "SortOrder");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Competitions_SortOrder",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "Group",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "Level",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "Tier",
                table: "Competitions");
        }
    }
}
