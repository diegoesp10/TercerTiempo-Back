using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LicensePlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.months_invalid", "es" });

            migrationBuilder.AddColumn<bool>(
                name: "AutoRenew",
                table: "TeamLicenses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CustomerRef",
                table: "TeamLicenses",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Interval",
                table: "TeamLicenses",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NextInterval",
                table: "TeamLicenses",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NextPlan",
                table: "TeamLicenses",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Plan",
                table: "TeamLicenses",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "TeamLicenses",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionRef",
                table: "TeamLicenses",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Interval",
                table: "LicensePayments",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Monthly");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "LicensePayments",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Subscription");

            migrationBuilder.AddColumn<int>(
                name: "PeriodPriceCents",
                table: "LicensePayments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Plan",
                table: "LicensePayments",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Basic");

            // Lo pagado antes de los planes era la licencia única de 10 €/mes, sin renovación automática: queda como
            // Básica mensual que no se renueva.
            migrationBuilder.Sql("UPDATE LicensePayments SET PeriodPriceCents = CASE WHEN Months > 0 THEN AmountCents / Months ELSE AmountCents END;");
            migrationBuilder.Sql("UPDATE TeamLicenses SET [Plan] = 'Basic', [Interval] = 'Monthly', Provider = 'mock' WHERE PaidUntil IS NOT NULL;");

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "license.no_subscription", "es", "Este equipo no tiene una suscripción que gestionar." },
                    { "license.plan_invalid", "es", "Plan o periodo de facturación no válido." },
                    { "license.plan_too_small", "es", "Este equipo tiene {0} en el cuerpo técnico y la licencia {1} admite {2}. Revoca nombramientos antes de elegirla." },
                    { "license.staff_limit", "es", "La licencia {0} admite {1} en el cuerpo técnico y este equipo ya está completo. Revoca algún nombramiento o pasa a Premium." },
                    { "license.subscription_active", "es", "Este equipo ya tiene una suscripción que se renueva sola: cambia su plan o cancela la renovación." },
                    { "license.webhook_invalid", "es", "Aviso de la pasarela de pago no válido." }
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLicenses_SubscriptionRef",
                table: "TeamLicenses",
                column: "SubscriptionRef");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TeamLicenses_SubscriptionRef",
                table: "TeamLicenses");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.no_subscription", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.plan_invalid", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.plan_too_small", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.staff_limit", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.subscription_active", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.webhook_invalid", "es" });

            migrationBuilder.DropColumn(
                name: "AutoRenew",
                table: "TeamLicenses");

            migrationBuilder.DropColumn(
                name: "CustomerRef",
                table: "TeamLicenses");

            migrationBuilder.DropColumn(
                name: "Interval",
                table: "TeamLicenses");

            migrationBuilder.DropColumn(
                name: "NextInterval",
                table: "TeamLicenses");

            migrationBuilder.DropColumn(
                name: "NextPlan",
                table: "TeamLicenses");

            migrationBuilder.DropColumn(
                name: "Plan",
                table: "TeamLicenses");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "TeamLicenses");

            migrationBuilder.DropColumn(
                name: "SubscriptionRef",
                table: "TeamLicenses");

            migrationBuilder.DropColumn(
                name: "Interval",
                table: "LicensePayments");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "LicensePayments");

            migrationBuilder.DropColumn(
                name: "PeriodPriceCents",
                table: "LicensePayments");

            migrationBuilder.DropColumn(
                name: "Plan",
                table: "LicensePayments");

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[] { "license.months_invalid", "es", "Puedes pagar entre 1 y {0} meses." });
        }
    }
}
