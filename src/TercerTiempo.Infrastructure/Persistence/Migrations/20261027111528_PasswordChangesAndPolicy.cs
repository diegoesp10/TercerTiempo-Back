using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PasswordChangesAndPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PasswordChangedAt",
                table: "AspNetUsers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "auth.current_password_wrong", "es", "La contraseña actual no es correcta." },
                    { "auth.locked_out", "es", "Demasiados intentos fallidos. Vuelve a intentarlo dentro de {0} minutos." },
                    { "auth.password_change_too_soon", "es", "Solo se puede cambiar la contraseña una vez al día. Podrás volver a cambiarla a partir del {0}." },
                    { "auth.password_same", "es", "La contraseña nueva tiene que ser distinta de la actual." },
                    { "auth.password_too_long", "es", "La contraseña no puede tener más de {0} caracteres." },
                    { "identity.PasswordContainsEmail", "es", "La contraseña no puede contener tu email." },
                    { "identity.PasswordMismatch", "es", "La contraseña actual no es correcta." },
                    { "identity.PasswordRepeatedChars", "es", "La contraseña no puede repetir el mismo carácter más de {0} veces seguidas." },
                    { "identity.PasswordTooCommon", "es", "Esa contraseña es demasiado fácil de adivinar: evita palabras como «contraseña», «fútbol» o «qwerty» y series como «123456»." },
                    { "identity.PasswordTooLong", "es", "La contraseña no puede tener más de {0} caracteres." }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "auth.current_password_wrong", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "auth.locked_out", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "auth.password_change_too_soon", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "auth.password_same", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "auth.password_too_long", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "identity.PasswordContainsEmail", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "identity.PasswordMismatch", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "identity.PasswordRepeatedChars", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "identity.PasswordTooCommon", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "identity.PasswordTooLong", "es" });

            migrationBuilder.DropColumn(
                name: "PasswordChangedAt",
                table: "AspNetUsers");
        }
    }
}
