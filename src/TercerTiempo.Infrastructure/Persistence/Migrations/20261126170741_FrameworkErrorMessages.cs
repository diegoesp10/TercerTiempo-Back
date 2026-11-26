using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FrameworkErrorMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "auth.admin_read_only", "es", "Con la cuenta de administrador solo se puede mirar: los cambios los hace cada club, técnico o jugador desde su cuenta." },
                    { "auth.forbidden", "es", "No tienes permiso para hacer esto." },
                    { "auth.session_expired", "es", "Tu sesión ha caducado o ya no es válida: vuelve a iniciar sesión." },
                    { "request.invalid", "es", "La petición no es válida." },
                    { "request.invalid_body", "es", "No se pueden leer los datos enviados: revisa su formato y el tipo de cada campo." },
                    { "request.invalid_parameter", "es", "El parámetro '{0}' no tiene un valor válido." },
                    { "request.method_not_allowed", "es", "Esta dirección no admite el método {0}." },
                    { "request.missing_parameter", "es", "Falta el parámetro '{0}'." },
                    { "request.not_found", "es", "No existe o ya no está disponible." },
                    { "request.too_large", "es", "Lo enviado ocupa más de lo que admite esta dirección." },
                    { "request.unsupported_media_type", "es", "Esta dirección no admite los datos en ese formato." }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "auth.admin_read_only", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "auth.forbidden", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "auth.session_expired", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "request.invalid", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "request.invalid_body", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "request.invalid_parameter", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "request.method_not_allowed", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "request.missing_parameter", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "request.not_found", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "request.too_large", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "request.unsupported_media_type", "es" });
        }
    }
}
