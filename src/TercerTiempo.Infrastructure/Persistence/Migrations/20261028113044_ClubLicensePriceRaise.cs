using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClubLicensePriceRaise : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "LicensePrices",
                keyColumn: "Id",
                keyValue: new Guid("01a16f00-0000-7000-8000-000000000011"),
                columns: new[] { "MonthlyPriceCents", "YearlyPriceCents" },
                values: new object[] { 990, 9900 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "LicensePrices",
                keyColumn: "Id",
                keyValue: new Guid("01a16f00-0000-7000-8000-000000000011"),
                columns: new[] { "MonthlyPriceCents", "YearlyPriceCents" },
                values: new object[] { 490, 4900 });
        }
    }
}
