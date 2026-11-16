using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RefereeNoVote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Score",
                table: "RefereeRatings",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "referee.no_vote_comment", "es", "«No votado» va sin comentario: si quieres explicar algo, pon una nota." },
                    { "referee.nothing_to_vote", "es", "Tu equipo ya ha contestado por todo el equipo arbitral de este partido." }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Antes no existía el «No votado»: al volver atrás, esas respuestas no caben en una nota obligatoria.
            migrationBuilder.Sql("DELETE FROM [RefereeRatings] WHERE [Score] IS NULL");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "referee.no_vote_comment", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "referee.nothing_to_vote", "es" });

            migrationBuilder.AlterColumn<int>(
                name: "Score",
                table: "RefereeRatings",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
