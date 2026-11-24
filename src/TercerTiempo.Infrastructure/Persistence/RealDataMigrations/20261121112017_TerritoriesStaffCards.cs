using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.RealDataMigrations
{
    /// <inheritdoc />
    public partial class TerritoriesStaffCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RedCard",
                table: "OfficialSheetStaff",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "YellowCards",
                table: "OfficialSheetStaff",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "TerritoryId",
                table: "Competitions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "Days",
                table: "CommitteeSanctions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "CommitteeSanctions",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Player");

            migrationBuilder.CreateTable(
                name: "Territories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Territories", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Territories",
                columns: new[] { "Id", "Kind", "Name" },
                values: new object[,]
                {
                    { new Guid("0b6f2a1e-7c34-4d8a-9f51-2e7d3c9a1b01"), "National", "Nacional" },
                    { new Guid("5d9e8c47-1a2b-4f6e-8c3d-7b4a2e1f9c33"), "Regional", "Asturias" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Competitions_TerritoryId",
                table: "Competitions",
                column: "TerritoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Territories_Name",
                table: "Territories",
                column: "Name",
                unique: true);

            // Las ligas que ya había: las de la RFEF, las profesionales y sus copas (y la del Rey), al ámbito nacional; las demás,
            // a Asturias, la única comunidad que se sincronizaba hasta ahora.
            migrationBuilder.Sql("""
                UPDATE [Competitions] SET [TerritoryId] = CASE
                    WHEN [Level] IN ('Professional', 'National')
                      OR ([Level] = 'Cup' AND ([Category] LIKE '%RFEF%' OR [Category] LIKE '%del Rey%'))
                    THEN '0b6f2a1e-7c34-4d8a-9f51-2e7d3c9a1b01'
                    ELSE '5d9e8c47-1a2b-4f6e-8c3d-7b4a2e1f9c33' END;
                """);

            // Las actas cerradas ya guardadas vuelven a leer su cuerpo técnico de la página guardada (sin pedir nada a la
            // federación) para sacar sus tarjetas; si la página ya no está, se queda como estaba.
            migrationBuilder.Sql("""
                UPDATE [OfficialSheets] SET [StaffReadAt] = NULL
                WHERE [Status] = 'Available' AND [Provider] = 'asturfutbol'
                  AND EXISTS (SELECT 1 FROM [OfficialSheetStaff] s WHERE s.[SheetId] = [OfficialSheets].[Id]);
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_Competitions_Territories_TerritoryId",
                table: "Competitions",
                column: "TerritoryId",
                principalTable: "Territories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Competitions_Territories_TerritoryId",
                table: "Competitions");

            migrationBuilder.DropTable(
                name: "Territories");

            migrationBuilder.DropIndex(
                name: "IX_Competitions_TerritoryId",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "RedCard",
                table: "OfficialSheetStaff");

            migrationBuilder.DropColumn(
                name: "YellowCards",
                table: "OfficialSheetStaff");

            migrationBuilder.DropColumn(
                name: "TerritoryId",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "Days",
                table: "CommitteeSanctions");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "CommitteeSanctions");
        }
    }
}
