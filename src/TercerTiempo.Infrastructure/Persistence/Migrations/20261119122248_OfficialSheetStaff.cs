using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OfficialSheetStaff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StaffReadAt",
                table: "OfficialSheets",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OfficialSheetStaff",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SheetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Side = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RoleLabel = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficialSheetStaff", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfficialSheetStaff_OfficialSheets_SheetId",
                        column: x => x.SheetId,
                        principalTable: "OfficialSheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OfficialSheetStaff_NormalizedName",
                table: "OfficialSheetStaff",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_OfficialSheetStaff_SheetId",
                table: "OfficialSheetStaff",
                column: "SheetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OfficialSheetStaff");

            migrationBuilder.DropColumn(
                name: "StaffReadAt",
                table: "OfficialSheets");
        }
    }
}
