using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClubLicensePerClub : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LicensePayments_TeamLicenses_LicenseId",
                table: "LicensePayments");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "club.team_name_mismatch", "es" });

            migrationBuilder.CreateTable(
                name: "ClubLicenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClubId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TrialEndsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    PaidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    Plan = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Interval = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    AutoRenew = table.Column<bool>(type: "bit", nullable: false),
                    NextPlan = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    NextInterval = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    CustomerRef = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    SubscriptionRef = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubLicenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClubLicenses_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Una licencia por club: se conserva la mejor de las que tenía por equipo (la suscripción en marcha y, si
            // no, la que más acceso da) y sus pagos pasan a ella.
            migrationBuilder.Sql("""
                WITH Ranked AS (
                    SELECT Id, ClubId, ROW_NUMBER() OVER (PARTITION BY ClubId ORDER BY
                        CASE WHEN AutoRenew = 1 AND [Plan] IS NOT NULL THEN 0 ELSE 1 END,
                        CASE WHEN PaidUntil > TrialEndsOn THEN PaidUntil ELSE TrialEndsOn END DESC,
                        CreatedAt) AS Position
                    FROM TeamLicenses)
                INSERT INTO ClubLicenses (Id, ClubId, TrialEndsOn, PaidUntil, [Plan], Interval, AutoRenew, NextPlan, NextInterval,
                    Provider, CustomerRef, SubscriptionRef, CreatedAt, UpdatedAt)
                SELECT l.Id, l.ClubId, l.TrialEndsOn, l.PaidUntil, l.[Plan], l.Interval, l.AutoRenew, l.NextPlan, l.NextInterval,
                    l.Provider, l.CustomerRef, l.SubscriptionRef, l.CreatedAt, l.UpdatedAt
                FROM TeamLicenses l JOIN Ranked r ON r.Id = l.Id
                WHERE r.Position = 1;

                UPDATE p SET LicenseId = c.Id
                FROM LicensePayments p
                JOIN TeamLicenses l ON l.Id = p.LicenseId
                JOIN ClubLicenses c ON c.ClubId = l.ClubId
                WHERE p.LicenseId <> c.Id;
                """);

            migrationBuilder.DropTable(
                name: "TeamLicenses");

            migrationBuilder.DropColumn(
                name: "TeamId",
                table: "LicensePayments");

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.plan_too_small", "es" },
                column: "Message",
                value: "{0} tiene {1} en el cuerpo técnico y la licencia {2} por equipo. Revoca nombramientos antes de elegirla.");

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.staff_limit", "es" },
                column: "Message",
                value: "La licencia {0} admite {1} en el cuerpo técnico de cada equipo y este ya está completo. Revoca algún nombramiento o pasa a Premium.");

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "club.last_team", "es", "El club tiene que conservar al menos un equipo." },
                    { "club.team_not_filial", "es", "{0} no es un equipo del {1}: la licencia del club solo cubre su primer equipo y sus filiales." }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClubLicenses_ClubId",
                table: "ClubLicenses",
                column: "ClubId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClubLicenses_SubscriptionRef",
                table: "ClubLicenses",
                column: "SubscriptionRef");

            migrationBuilder.AddForeignKey(
                name: "FK_LicensePayments_ClubLicenses_LicenseId",
                table: "LicensePayments",
                column: "LicenseId",
                principalTable: "ClubLicenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LicensePayments_ClubLicenses_LicenseId",
                table: "LicensePayments");

            migrationBuilder.DropTable(
                name: "ClubLicenses");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "club.last_team", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "club.team_not_filial", "es" });

            migrationBuilder.AddColumn<Guid>(
                name: "TeamId",
                table: "LicensePayments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "TeamLicenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AutoRenew = table.Column<bool>(type: "bit", nullable: false),
                    ClubId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CustomerRef = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Interval = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    NextInterval = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    NextPlan = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PaidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    Plan = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    SubscriptionRef = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    TeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TrialEndsOn = table.Column<DateOnly>(type: "date", nullable: false),
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

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.plan_too_small", "es" },
                column: "Message",
                value: "Este equipo tiene {0} en el cuerpo técnico y la licencia {1} admite {2}. Revoca nombramientos antes de elegirla.");

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "license.staff_limit", "es" },
                column: "Message",
                value: "La licencia {0} admite {1} en el cuerpo técnico y este equipo ya está completo. Revoca algún nombramiento o pasa a Premium.");

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[] { "club.team_name_mismatch", "es", "{0} no se corresponde con el nombre del club ({1})." });

            migrationBuilder.CreateIndex(
                name: "IX_TeamLicenses_ClubId",
                table: "TeamLicenses",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLicenses_SubscriptionRef",
                table: "TeamLicenses",
                column: "SubscriptionRef");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLicenses_TeamId",
                table: "TeamLicenses",
                column: "TeamId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LicensePayments_TeamLicenses_LicenseId",
                table: "LicensePayments",
                column: "LicenseId",
                principalTable: "TeamLicenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
