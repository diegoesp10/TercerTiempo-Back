using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TeamLicenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TeamLicenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClubId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TrialEndsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    PaidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLicenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamLicenses_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLicenses_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LicensePayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClubId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Months = table.Column<int>(type: "int", nullable: false),
                    AmountCents = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SessionId = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicensePayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LicensePayments_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LicensePayments_TeamLicenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "TeamLicenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "license.months_invalid", "es", "Puedes pagar entre 1 y {0} meses." },
                    { "license.not_found", "es", "La licencia '{0}' no existe." },
                    { "license.payment_already_paid", "es", "Ese pago ya está confirmado." },
                    { "license.payment_cancelled", "es", "Ese pago se canceló; inicia uno nuevo." },
                    { "license.payment_not_confirmed", "es", "La pasarela no ha confirmado el pago todavía." },
                    { "license.payment_not_found", "es", "El pago '{0}' no existe o no es de tu club." },
                    { "license.required", "es", "Este equipo no tiene licencia vigente en Tercer Tiempo. Sus datos se conservan, pero el club debe renovar la licencia para que el cuerpo técnico y los jugadores vuelvan a verlos." },
                    { "license.viewer_required", "es", "Para ver las actas internas y los vídeos de otros equipos tu cuenta debe pertenecer a un equipo con licencia vigente." }
                });

            migrationBuilder.CreateIndex(
                name: "IX_LicensePayments_ClubId",
                table: "LicensePayments",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_LicensePayments_LicenseId",
                table: "LicensePayments",
                column: "LicenseId");

            migrationBuilder.CreateIndex(
                name: "IX_LicensePayments_SessionId",
                table: "LicensePayments",
                column: "SessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamLicenses_ClubId",
                table: "TeamLicenses",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLicenses_TeamId",
                table: "TeamLicenses",
                column: "TeamId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LicensePayments");

            migrationBuilder.DropTable(
                name: "TeamLicenses");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.months_invalid", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.not_found", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.payment_already_paid", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.payment_cancelled", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.payment_not_confirmed", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.payment_not_found", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.required", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.viewer_required", "es" });
        }
    }
}
