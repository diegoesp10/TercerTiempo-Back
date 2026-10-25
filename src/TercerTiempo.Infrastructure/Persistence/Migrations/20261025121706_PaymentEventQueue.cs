using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaymentEventQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FailureReason",
                table: "LicensePayments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MockCheckoutSessions",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    LicenseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AmountCents = table.Column<int>(type: "int", nullable: false),
                    PeriodPriceCents = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Months = table.Column<int>(type: "int", nullable: false),
                    DeferUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    CustomerRef = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    SubscriptionRef = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastDecline = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockCheckoutSessions", x => x.SessionId);
                });

            migrationBuilder.CreateTable(
                name: "PaymentEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentEvents", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[] { "license.simulator_disabled", "es", "La simulación de pagos solo funciona con la pasarela simulada (Licensing:Provider = mock)." });

            migrationBuilder.CreateIndex(
                name: "IX_MockCheckoutSessions_LicenseId",
                table: "MockCheckoutSessions",
                column: "LicenseId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_LicenseId_ReceivedAt",
                table: "PaymentEvents",
                columns: new[] { "LicenseId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_Provider_ExternalId",
                table: "PaymentEvents",
                columns: new[] { "Provider", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_Status_NextAttemptAt",
                table: "PaymentEvents",
                columns: new[] { "Status", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MockCheckoutSessions");

            migrationBuilder.DropTable(
                name: "PaymentEvents");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.simulator_disabled", "es" });

            migrationBuilder.DropColumn(
                name: "FailureReason",
                table: "LicensePayments");
        }
    }
}
