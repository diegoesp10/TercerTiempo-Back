using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JobRunsAndAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JobRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Job = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Manual = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Errors = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Requests = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScrapeRequests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: true),
                    DurationMs = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    JobRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScrapeRequests", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "admin.invalid_credentials", "es", "Usuario o contraseña incorrectos." },
                    { "job.already_running", "es", "El trabajo '{0}' ya está en marcha." },
                    { "job.disabled", "es", "El trabajo '{0}' está desactivado en la configuración." },
                    { "job.not_found", "es", "El trabajo '{0}' no existe." }
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobRuns_Job_StartedAt",
                table: "JobRuns",
                columns: new[] { "Job", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ScrapeRequests_At",
                table: "ScrapeRequests",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_ScrapeRequests_JobRunId",
                table: "ScrapeRequests",
                column: "JobRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ScrapeRequests_Provider_At",
                table: "ScrapeRequests",
                columns: new[] { "Provider", "At" });

            // Punto de partida del historial: la base ya tiene los datos cargados, así que la primera ejecución
            // automática de cada trabajo llega un intervalo después de aplicar esta migración y no al arrancar.
            migrationBuilder.Sql("""
                INSERT INTO [JobRuns] ([Id], [Job], [Manual], [Status], [StartedAt], [FinishedAt], [Summary], [Errors], [Requests])
                VALUES
                    (NEWID(), 'football-data', 0, 'succeeded', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 'Punto de partida: datos cargados antes de llevar el historial', NULL, 0),
                    (NEWID(), 'official-sheets', 0, 'succeeded', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 'Punto de partida: actas cargadas antes de llevar el historial', NULL, 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobRuns");

            migrationBuilder.DropTable(
                name: "ScrapeRequests");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "admin.invalid_credentials", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "job.already_running", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "job.disabled", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "job.not_found", "es" });
        }
    }
}
