using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeagueSuspensionsAdultsAndBrowserStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "CookieDefinitions",
                columns: new[] { "Id", "Category", "Duration", "Name", "Provider", "Purpose", "SortOrder", "Storage" },
                values: new object[,]
                {
                    { new Guid("01a16f00-0000-7000-8000-000000000111"), "Necessary", "Hasta cerrar sesión", "tt.active-session", "Tercer Tiempo", "Deja una sola sesión abierta en el navegador (la de tu cuenta o la del administrador) en todas tus pestañas.", 50, "LocalStorage" },
                    { new Guid("01a16f00-0000-7000-8000-000000000112"), "Necessary", "Hasta cerrar la pestaña", "tt.reloaded-for", "Tercer Tiempo", "Evita recargar la página más de una vez cuando se publica una versión nueva de la web.", 60, "SessionStorage" },
                    { new Guid("01a16f00-0000-7000-8000-000000000113"), "Necessary", "Hasta terminar o cancelar la subida", "tt.video-upload.*", "Tercer Tiempo", "Permite reanudar la subida de un vídeo de partido si se corta: nombre y tamaño del fichero y hasta dónde se envió.", 70, "LocalStorage" },
                    { new Guid("01a16f00-0000-7000-8000-000000000114"), "Preferences", "Permanente, hasta que la borres", "tt.search-recent", "Tercer Tiempo", "Tus últimas búsquedas, para repetirlas con un clic.", 50, "LocalStorage" },
                    { new Guid("01a16f00-0000-7000-8000-000000000115"), "Preferences", "Permanente, hasta que la borres", "tt.calendar.view", "Tercer Tiempo", "Vista del calendario de la plantilla que prefieres (mes, semana, día o lista).", 60, "LocalStorage" },
                    { new Guid("01a16f00-0000-7000-8000-000000000116"), "Preferences", "Permanente, hasta que la borres", "tt.attendance.view", "Tercer Tiempo", "Vista de la asistencia a los entrenamientos que prefieres.", 70, "LocalStorage" },
                    { new Guid("01a16f00-0000-7000-8000-000000000117"), "Preferences", "Permanente, hasta que la borres", "tt.group.*", "Tercer Tiempo", "Si quieres cada lista de jugadores agrupada por posición.", 80, "LocalStorage" }
                });

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "player.min_age", "es" },
                column: "Message",
                value: "Tienes que ser mayor de edad para tener una cuenta.");

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "callup.player_suspended", "es", "{0} está sancionado para este partido ({1}): no puede ir convocado. Quítalo de la lista." },
                    { "sheet.player_suspended", "es", "{0} estaba sancionado para este partido ({1}): no puede salir en el acta como que jugó." }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "CookieDefinitions",
                keyColumn: "Id",
                keyValue: new Guid("01a16f00-0000-7000-8000-000000000111"));

            migrationBuilder.DeleteData(
                table: "CookieDefinitions",
                keyColumn: "Id",
                keyValue: new Guid("01a16f00-0000-7000-8000-000000000112"));

            migrationBuilder.DeleteData(
                table: "CookieDefinitions",
                keyColumn: "Id",
                keyValue: new Guid("01a16f00-0000-7000-8000-000000000113"));

            migrationBuilder.DeleteData(
                table: "CookieDefinitions",
                keyColumn: "Id",
                keyValue: new Guid("01a16f00-0000-7000-8000-000000000114"));

            migrationBuilder.DeleteData(
                table: "CookieDefinitions",
                keyColumn: "Id",
                keyValue: new Guid("01a16f00-0000-7000-8000-000000000115"));

            migrationBuilder.DeleteData(
                table: "CookieDefinitions",
                keyColumn: "Id",
                keyValue: new Guid("01a16f00-0000-7000-8000-000000000116"));

            migrationBuilder.DeleteData(
                table: "CookieDefinitions",
                keyColumn: "Id",
                keyValue: new Guid("01a16f00-0000-7000-8000-000000000117"));

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "callup.player_suspended", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "sheet.player_suspended", "es" });

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "player.min_age", "es" },
                column: "Message",
                value: "El jugador debe tener al menos 14 años.");
        }
    }
}
