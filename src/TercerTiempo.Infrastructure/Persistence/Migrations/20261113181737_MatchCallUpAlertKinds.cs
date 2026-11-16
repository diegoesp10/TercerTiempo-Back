using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MatchCallUpAlertKinds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Los avisos de convocatoria son solo de partidos: el tipo lo dice ya en el nombre.
            migrationBuilder.Sql("UPDATE [Alerts] SET [Kind] = N'MatchCallUp' WHERE [Kind] = N'CallUp';");
            migrationBuilder.Sql("UPDATE [Alerts] SET [Kind] = N'MatchNotCalledUp' WHERE [Kind] = N'NotCalledUp';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [Alerts] SET [Kind] = N'CallUp' WHERE [Kind] = N'MatchCallUp';");
            migrationBuilder.Sql("UPDATE [Alerts] SET [Kind] = N'NotCalledUp' WHERE [Kind] = N'MatchNotCalledUp';");
        }
    }
}
