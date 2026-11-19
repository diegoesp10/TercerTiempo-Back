using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RatingOnPitchAndVideoActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ActionsFromVideo",
                table: "PlayerMatchPerformances",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "GoalsConcededOnPitch",
                table: "PlayerMatchPerformances",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActionsFromVideo",
                table: "PlayerMatchPerformances");

            migrationBuilder.DropColumn(
                name: "GoalsConcededOnPitch",
                table: "PlayerMatchPerformances");
        }
    }
}
