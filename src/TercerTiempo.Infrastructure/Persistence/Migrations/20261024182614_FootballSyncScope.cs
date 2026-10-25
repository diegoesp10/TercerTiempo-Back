using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FootballSyncScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScopeKind",
                table: "JobRuns",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScopeLabel",
                table: "JobRuns",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FootballSyncSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScheduledScope = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DaysBack = table.Column<int>(type: "int", nullable: false),
                    DaysAhead = table.Column<int>(type: "int", nullable: false),
                    RostersInRound = table.Column<bool>(type: "bit", nullable: false),
                    FullSyncEveryDays = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FootballSyncSettings", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[] { "sync.scheduled_scope_invalid", "es", "La pasada automática solo puede ser la jornada en curso o la temporada completa." });

            migrationBuilder.InsertData(
                table: "FootballSyncSettings",
                columns: new[] { "Id", "DaysAhead", "DaysBack", "FullSyncEveryDays", "RostersInRound", "ScheduledScope", "UpdatedAt" },
                values: new object[] { new Guid("01a16f00-0000-7000-8000-000000000002"), 4, 3, 7, false, "CurrentRound", new DateTimeOffset(new DateTime(2026, 10, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });

            // Hasta ahora cada sincronización de las ligas era la temporada entera con plantillas.
            migrationBuilder.Sql("""
                UPDATE [JobRuns] SET [ScopeKind] = N'FullSeason', [ScopeLabel] = N'Temporada completa'
                WHERE [Job] = N'football-data' AND [ScopeKind] IS NULL
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FootballSyncSettings");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "sync.scheduled_scope_invalid", "es" });

            migrationBuilder.DropColumn(
                name: "ScopeKind",
                table: "JobRuns");

            migrationBuilder.DropColumn(
                name: "ScopeLabel",
                table: "JobRuns");
        }
    }
}
