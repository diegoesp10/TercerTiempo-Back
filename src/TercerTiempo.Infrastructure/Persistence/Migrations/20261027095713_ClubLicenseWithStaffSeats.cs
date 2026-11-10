using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClubLicenseWithStaffSeats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LicensePlanSettings");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.plan_too_small", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "licensing.plan_order", "es" });

            migrationBuilder.AddColumn<int>(
                name: "MaxStaffSeats",
                table: "LicensingSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TrialStaffSeats",
                table: "LicensingSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StaffSeats",
                table: "LicensePayments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "NextStaffSeats",
                table: "ClubLicenses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StaffSeats",
                table: "ClubLicenses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Lo ya contratado se conserva: la licencia Básica era el club con 1 técnico y la Premium, con 6.
            migrationBuilder.Sql("UPDATE ClubLicenses SET StaffSeats = CASE [Plan] WHEN 'Premium' THEN 6 WHEN 'Basic' THEN 1 ELSE 0 END, NextStaffSeats = CASE NextPlan WHEN 'Premium' THEN 6 WHEN 'Basic' THEN 1 ELSE NULL END");
            migrationBuilder.Sql("UPDATE LicensePayments SET StaffSeats = CASE [Plan] WHEN 'Premium' THEN 6 WHEN 'Basic' THEN 1 ELSE 0 END");

            migrationBuilder.DropColumn(
                name: "Plan",
                table: "LicensePayments");

            migrationBuilder.DropColumn(
                name: "NextPlan",
                table: "ClubLicenses");

            migrationBuilder.DropColumn(
                name: "Plan",
                table: "ClubLicenses");

            migrationBuilder.CreateTable(
                name: "LicensePrices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SettingsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Item = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MonthlyPriceCents = table.Column<int>(type: "int", nullable: false),
                    YearlyPriceCents = table.Column<int>(type: "int", nullable: false),
                    StripeMonthlyPriceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StripeYearlyPriceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicensePrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LicensePrices_LicensingSettings_SettingsId",
                        column: x => x.SettingsId,
                        principalTable: "LicensingSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.no_subscription", "es" },
                column: "Message",
                value: "El club no tiene una suscripción en marcha que modificar: contrata la licencia o reactiva su renovación.");

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.plan_invalid", "es" },
                column: "Message",
                value: "Periodo de facturación o concepto de la licencia no válido.");

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.staff_limit", "es" },
                column: "Message",
                value: "La licencia del club incluye {0} y ya están todos asignados. Añade técnicos a la licencia o revoca algún nombramiento.");

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.subscription_active", "es" },
                column: "Message",
                value: "El club ya tiene una suscripción que se renueva sola: cambia sus técnicos o su facturación, o cancela la renovación.");

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "license.club_required", "es", "Las licencias de técnico se añaden una vez contratada la licencia de club." },
                    { "license.staff_seats_range", "es", "Un club puede tener entre 0 y {0} licencias de técnico." },
                    { "license.staff_seats_too_few", "es", "El club tiene {0} en el cuerpo técnico de sus equipos y la licencia se quedaría con {1}. Revoca nombramientos antes de reducirla." }
                });

            migrationBuilder.InsertData(
                table: "LicensePrices",
                columns: new[] { "Id", "Item", "MonthlyPriceCents", "SettingsId", "StripeMonthlyPriceId", "StripeYearlyPriceId", "YearlyPriceCents" },
                values: new object[,]
                {
                    { new Guid("01a16f00-0000-7000-8000-000000000011"), "Club", 490, new Guid("01a16f00-0000-7000-8000-000000000001"), null, null, 4900 },
                    { new Guid("01a16f00-0000-7000-8000-000000000012"), "StaffSeat", 250, new Guid("01a16f00-0000-7000-8000-000000000001"), null, null, 2500 }
                });

            migrationBuilder.UpdateData(
                table: "LicensingSettings",
                keyColumn: "Id",
                keyValue: new Guid("01a16f00-0000-7000-8000-000000000001"),
                columns: new[] { "MaxStaffSeats", "TrialStaffSeats", "UpdatedAt" },
                values: new object[] { 30, 2, new DateTimeOffset(new DateTime(2026, 10, 27, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });

            migrationBuilder.CreateIndex(
                name: "IX_LicensePrices_SettingsId_Item",
                table: "LicensePrices",
                columns: new[] { "SettingsId", "Item" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LicensePrices");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.club_required", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.staff_seats_range", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.staff_seats_too_few", "es" });

            migrationBuilder.DropColumn(
                name: "MaxStaffSeats",
                table: "LicensingSettings");

            migrationBuilder.DropColumn(
                name: "TrialStaffSeats",
                table: "LicensingSettings");

            migrationBuilder.DropColumn(
                name: "StaffSeats",
                table: "LicensePayments");

            migrationBuilder.DropColumn(
                name: "NextStaffSeats",
                table: "ClubLicenses");

            migrationBuilder.DropColumn(
                name: "StaffSeats",
                table: "ClubLicenses");

            migrationBuilder.AddColumn<string>(
                name: "Plan",
                table: "LicensePayments",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NextPlan",
                table: "ClubLicenses",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Plan",
                table: "ClubLicenses",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LicensePlanSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaxStaff = table.Column<int>(type: "int", nullable: false),
                    MonthlyPriceCents = table.Column<int>(type: "int", nullable: false),
                    Plan = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SettingsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StripeMonthlyPriceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StripeYearlyPriceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    YearlyPriceCents = table.Column<int>(type: "int", nullable: false)
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

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.no_subscription", "es" },
                column: "Message",
                value: "Este equipo no tiene una suscripción que gestionar.");

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.plan_invalid", "es" },
                column: "Message",
                value: "Plan o periodo de facturación no válido.");

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.staff_limit", "es" },
                column: "Message",
                value: "La licencia {0} admite {1} en el cuerpo técnico de cada equipo y este ya está completo. Revoca algún nombramiento o pasa a Premium.");

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.subscription_active", "es" },
                column: "Message",
                value: "Este equipo ya tiene una suscripción que se renueva sola: cambia su plan o cancela la renovación.");

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "license.plan_too_small", "es", "{0} tiene {1} en el cuerpo técnico y la licencia {2} por equipo. Revoca nombramientos antes de elegirla." },
                    { "licensing.plan_order", "es", "Premium no puede admitir menos técnicos que Básica." }
                });

            migrationBuilder.InsertData(
                table: "LicensePlanSettings",
                columns: new[] { "Id", "MaxStaff", "MonthlyPriceCents", "Plan", "SettingsId", "StripeMonthlyPriceId", "StripeYearlyPriceId", "YearlyPriceCents" },
                values: new object[,]
                {
                    { new Guid("01a16f00-0000-7000-8000-000000000011"), 1, 990, "Basic", new Guid("01a16f00-0000-7000-8000-000000000001"), null, null, 9900 },
                    { new Guid("01a16f00-0000-7000-8000-000000000012"), 6, 1990, "Premium", new Guid("01a16f00-0000-7000-8000-000000000001"), null, null, 19900 }
                });

            migrationBuilder.UpdateData(
                table: "LicensingSettings",
                keyColumn: "Id",
                keyValue: new Guid("01a16f00-0000-7000-8000-000000000001"),
                column: "UpdatedAt",
                value: new DateTimeOffset(new DateTime(2026, 10, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.CreateIndex(
                name: "IX_LicensePlanSettings_SettingsId_Plan",
                table: "LicensePlanSettings",
                columns: new[] { "SettingsId", "Plan" },
                unique: true);
        }
    }
}
