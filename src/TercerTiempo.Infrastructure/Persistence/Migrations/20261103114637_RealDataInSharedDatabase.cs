using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RealDataInSharedDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // El estado de las descargas pasa a la base de datos reales (RealDataMigrations), que se llena con el de la
            // base de desarrollo antes de esta migración (RealData:SeedFromThisEnvironment). En la base de cada entorno
            // ya no se usa: se quita para que nadie lo tome por bueno. Down no lo recrea; vive en la base real.
            foreach (var table in new[] { "SheetDownloads", "MatchOfficialLookups", "ScrapeRequests", "FederationHttpCache", "FederationRequestStates", "FootballSyncSettings" })
                migrationBuilder.Sql($"DROP TABLE IF EXISTS [dbo].[{table}];");

            migrationBuilder.AddColumn<string>(
                name: "Environment",
                table: "JobRuns",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[] { "job.real_data_elsewhere", "es", "Los datos reales los está descargando otro entorno ({1}): lanza '{0}' desde ese entorno." });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "job.real_data_elsewhere", "es" });

            migrationBuilder.DropColumn(
                name: "Environment",
                table: "JobRuns");
        }
    }
}
