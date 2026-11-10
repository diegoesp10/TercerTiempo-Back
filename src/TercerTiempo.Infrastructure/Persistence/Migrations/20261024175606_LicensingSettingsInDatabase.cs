using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LicensingSettingsInDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LicensingSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    TrialDays = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicensingSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LicensePlanSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SettingsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Plan = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MonthlyPriceCents = table.Column<int>(type: "int", nullable: false),
                    YearlyPriceCents = table.Column<int>(type: "int", nullable: false),
                    MaxStaff = table.Column<int>(type: "int", nullable: false),
                    StripeMonthlyPriceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StripeYearlyPriceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicensePlanSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LicensePlanSettings_LicensingSettings_SettingsId",
                        column: x => x.SettingsId,
                        principalTable: "LicensingSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "licensing.currency_invalid", "es", "La moneda '{0}' no es un código ISO de tres letras (EUR)." },
                    { "licensing.plan_order", "es", "Premium no puede admitir menos técnicos que Básica." },
                    { "licensing.stripe_price_invalid", "es", "'{0}' no es un precio de Stripe: sus ids empiezan por price_." }
                });

            migrationBuilder.InsertData(
                table: "LicensingSettings",
                columns: new[] { "Id", "Currency", "TrialDays", "UpdatedAt" },
                values: new object[] { new Guid("01a16f00-0000-7000-8000-000000000001"), "EUR", 30, new DateTimeOffset(new DateTime(2026, 10, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });

            migrationBuilder.InsertData(
                table: "LicensePlanSettings",
                columns: new[] { "Id", "MaxStaff", "MonthlyPriceCents", "Plan", "SettingsId", "StripeMonthlyPriceId", "StripeYearlyPriceId", "YearlyPriceCents" },
                values: new object[,]
                {
                    { new Guid("01a16f00-0000-7000-8000-000000000011"), 1, 990, "Basic", new Guid("01a16f00-0000-7000-8000-000000000001"), null, null, 9900 },
                    { new Guid("01a16f00-0000-7000-8000-000000000012"), 6, 1990, "Premium", new Guid("01a16f00-0000-7000-8000-000000000001"), null, null, 19900 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlanSettings_SettingsId_Plan",
                table: "LicensePlanSettings",
                columns: new[] { "SettingsId", "Plan" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LicensePlanSettings");

            migrationBuilder.DropTable(
                name: "LicensingSettings");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "licensing.currency_invalid", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "licensing.plan_order", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "licensing.stripe_price_invalid", "es" });
        }
    }
}
