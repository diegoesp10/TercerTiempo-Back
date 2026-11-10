using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CookiesAndAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalyticsEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    VisitorId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    SessionId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Path = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    RouteName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EntityType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Actor = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ActorClubId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubjectClubId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalyticsEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CookieConsents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VisitorId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Analytics = table.Column<bool>(type: "bit", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PolicyVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CookieConsents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CookieDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Storage = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Duration = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CookieDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CookiePolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CookiePolicies", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "CookieDefinitions",
                columns: new[] { "Id", "Category", "Duration", "Name", "Provider", "Purpose", "SortOrder", "Storage" },
                values: new object[,]
                {
                    { new Guid("01a16f00-0000-7000-8000-000000000101"), "Necessary", "Hasta cerrar sesión", "tt.token", "Tercer Tiempo", "Mantiene la sesión iniciada (token de acceso).", 10, "LocalStorage" },
                    { new Guid("01a16f00-0000-7000-8000-000000000102"), "Necessary", "Hasta cerrar sesión", "tt.account", "Tercer Tiempo", "Datos de tu cuenta para mostrar la web sin esperar a la API.", 20, "LocalStorage" },
                    { new Guid("01a16f00-0000-7000-8000-000000000103"), "Necessary", "13 meses", "tt.consent", "Tercer Tiempo", "Guarda tu elección sobre las cookies para no volver a preguntarte.", 30, "LocalStorage" },
                    { new Guid("01a16f00-0000-7000-8000-000000000104"), "Necessary", "Hasta cerrar la pestaña", "tt.admin-session", "Tercer Tiempo", "Sesión del panel de administración.", 40, "SessionStorage" },
                    { new Guid("01a16f00-0000-7000-8000-000000000105"), "Preferences", "Permanente, hasta que la borres", "tt.theme", "Tercer Tiempo", "Tema claro u oscuro que eliges.", 10, "LocalStorage" },
                    { new Guid("01a16f00-0000-7000-8000-000000000106"), "Preferences", "Permanente, hasta que la borres", "tt.season", "Tercer Tiempo", "Temporada que estás consultando.", 20, "LocalStorage" },
                    { new Guid("01a16f00-0000-7000-8000-000000000107"), "Preferences", "Permanente, hasta que la borres", "tt.my-club-first", "Tercer Tiempo", "Si tu club sale primero en los listados.", 30, "LocalStorage" },
                    { new Guid("01a16f00-0000-7000-8000-000000000108"), "Preferences", "Permanente, hasta que la borres", "tt.home-competition", "Tercer Tiempo", "Última liga que has consultado en la portada.", 40, "LocalStorage" },
                    { new Guid("01a16f00-0000-7000-8000-000000000109"), "Analytics", "13 meses", "tt.aid", "Tercer Tiempo", "Identificador anónimo de tu navegador para contar visitantes, páginas vistas, búsquedas y clics. Los datos se quedan en Tercer Tiempo: no se envían a terceros.", 10, "LocalStorage" },
                    { new Guid("01a16f00-0000-7000-8000-000000000110"), "Analytics", "Hasta cerrar la pestaña", "tt.sid", "Tercer Tiempo", "Identificador anónimo de cada visita, para contar sesiones.", 20, "SessionStorage" }
                });

            migrationBuilder.InsertData(
                table: "CookiePolicies",
                columns: new[] { "Id", "UpdatedAt", "Version" },
                values: new object[] { new Guid("01a16f00-0000-7000-8000-000000000003"), new DateTimeOffset(new DateTime(2026, 10, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "2026-10-24" });

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "cookie.invalid", "es", "Categoría o almacenamiento de cookie no válidos." },
                    { "cookie.not_found", "es", "La cookie '{0}' no existe." },
                    { "cookie.visitor_invalid", "es", "Identificador de visitante no válido." }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_ActorClubId_OccurredAt",
                table: "AnalyticsEvents",
                columns: new[] { "ActorClubId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_EntityType_EntityId",
                table: "AnalyticsEvents",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_OccurredAt_Type",
                table: "AnalyticsEvents",
                columns: new[] { "OccurredAt", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_SubjectClubId_OccurredAt",
                table: "AnalyticsEvents",
                columns: new[] { "SubjectClubId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CookieConsents_CreatedAt",
                table: "CookieConsents",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CookieConsents_VisitorId_CreatedAt",
                table: "CookieConsents",
                columns: new[] { "VisitorId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CookieDefinitions_Name",
                table: "CookieDefinitions",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalyticsEvents");

            migrationBuilder.DropTable(
                name: "CookieConsents");

            migrationBuilder.DropTable(
                name: "CookieDefinitions");

            migrationBuilder.DropTable(
                name: "CookiePolicies");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "cookie.invalid", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "cookie.not_found", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "cookie.visitor_invalid", "es" });
        }
    }
}
