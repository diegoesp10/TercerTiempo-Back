using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeasonYearAsParent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SeasonYearId",
                table: "Seasons",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "SeasonYears",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonYears", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "season.closed", "es", "La temporada {0} está cerrada: solo se puede consultar. Cambia a la temporada actual ({1}) para editar." },
                    { "season.not_found", "es", "La temporada '{0}' no existe." }
                });

            // Una temporada de la app por cada nombre de temporada de liga ya guardado; la actual, la más reciente de las
            // que alguna liga tiene en curso. Después cada temporada de liga se cuelga de la suya.
            migrationBuilder.Sql("""
                INSERT INTO [SeasonYears] ([Id], [Label], [StartsOn], [EndsOn], [IsCurrent])
                SELECT NEWID(), [Label], MIN([StartsOn]), MAX([EndsOn]), 0 FROM [Seasons] GROUP BY [Label];

                UPDATE [SeasonYears] SET [IsCurrent] = 1
                WHERE [Id] = (SELECT TOP 1 y.[Id] FROM [SeasonYears] y
                              WHERE EXISTS (SELECT 1 FROM [Seasons] s WHERE s.[Label] = y.[Label] AND s.[IsCurrent] = 1)
                              ORDER BY y.[StartsOn] DESC);

                UPDATE s SET s.[SeasonYearId] = y.[Id]
                FROM [Seasons] s JOIN [SeasonYears] y ON y.[Label] = s.[Label];
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_SeasonYearId_CompetitionId",
                table: "Seasons",
                columns: new[] { "SeasonYearId", "CompetitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Matches_SeasonId_KickoffAt",
                table: "Matches",
                columns: new[] { "SeasonId", "KickoffAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SeasonYears_IsCurrent",
                table: "SeasonYears",
                column: "IsCurrent",
                unique: true,
                filter: "[IsCurrent] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_SeasonYears_Label",
                table: "SeasonYears",
                column: "Label",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Seasons_SeasonYears_SeasonYearId",
                table: "Seasons",
                column: "SeasonYearId",
                principalTable: "SeasonYears",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Seasons_SeasonYears_SeasonYearId",
                table: "Seasons");

            migrationBuilder.DropTable(
                name: "SeasonYears");

            migrationBuilder.DropIndex(
                name: "IX_Seasons_SeasonYearId_CompetitionId",
                table: "Seasons");

            migrationBuilder.DropIndex(
                name: "IX_Matches_SeasonId_KickoffAt",
                table: "Matches");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "season.closed", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "season.not_found", "es" });

            migrationBuilder.DropColumn(
                name: "SeasonYearId",
                table: "Seasons");
        }
    }
}
