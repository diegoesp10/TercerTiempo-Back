using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InjuryCarryOver : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ContinuedAt",
                table: "PlayerInjuries",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ContinuesInjuryId",
                table: "PlayerInjuries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[] { "injury.continued", "es", "Esa lesión pasó sin el alta a la temporada siguiente: aquí queda como historial (se consulta y se puede borrar) y se lleva desde la plantilla de esa temporada." });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerInjuries_ContinuesInjuryId",
                table: "PlayerInjuries",
                column: "ContinuesInjuryId",
                unique: true,
                filter: "[ContinuesInjuryId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerInjuries_PlayerInjuries_ContinuesInjuryId",
                table: "PlayerInjuries",
                column: "ContinuesInjuryId",
                principalTable: "PlayerInjuries",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlayerInjuries_PlayerInjuries_ContinuesInjuryId",
                table: "PlayerInjuries");

            migrationBuilder.DropIndex(
                name: "IX_PlayerInjuries_ContinuesInjuryId",
                table: "PlayerInjuries");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury.continued", "es" });

            migrationBuilder.DropColumn(
                name: "ContinuedAt",
                table: "PlayerInjuries");

            migrationBuilder.DropColumn(
                name: "ContinuesInjuryId",
                table: "PlayerInjuries");
        }
    }
}
