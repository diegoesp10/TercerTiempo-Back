using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RefereeRatingsFinalWithRater : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RatedByUserId",
                table: "RefereeRatings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RaterName",
                table: "RefereeRatings",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RaterStaffRole",
                table: "RefereeRatings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "referee.staff_only", "es" },
                column: "Message",
                value: "Solo los clubes y sus cuerpos técnicos, con licencia vigente, puntúan a los árbitros.");

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "referee.already_rated", "es", "Tu equipo ya puntuó a {0} en este partido y las notas son definitivas: no se pueden cambiar." },
                    { "referee.rating_not_confirmed", "es", "Confirma la nota antes de guardarla: una vez guardada no se puede cambiar." }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "referee.already_rated", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "referee.rating_not_confirmed", "es" });

            migrationBuilder.DropColumn(
                name: "RatedByUserId",
                table: "RefereeRatings");

            migrationBuilder.DropColumn(
                name: "RaterName",
                table: "RefereeRatings");

            migrationBuilder.DropColumn(
                name: "RaterStaffRole",
                table: "RefereeRatings");

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "referee.staff_only", "es" },
                column: "Message",
                value: "Los árbitros y sus notas son para los clubes y sus cuerpos técnicos, con licencia vigente.");
        }
    }
}
