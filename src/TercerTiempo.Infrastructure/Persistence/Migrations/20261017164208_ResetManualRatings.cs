using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Las notas que ponía el cuerpo técnico dejan de valer: se vacían y, al arrancar, la API las calcula con los datos
    /// de cada línea (RecalculateMissingRatingsCommand). Va en su propia migración porque en SQLite no se puede mezclar
    /// SQL suelto con la reconstrucción de tabla de la migración anterior.
    /// </summary>
    public partial class ResetManualRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE PlayerMatchPerformances SET Rating = NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Las notas manuales no se pueden recuperar.
        }
    }
}
