using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConfirmedAt",
                table: "AspNetUsers",
                type: "datetimeoffset",
                nullable: true);

            // Las cuentas que ya tienen perfil (club, técnico o jugador) y el administrador quedan confirmadas. Las que se
            // quedaron sin perfil por un alta cortada siguen sin confirmar: no entran y su email vuelve a estar libre.
            migrationBuilder.Sql("""
                UPDATE u SET ConfirmedAt = SYSDATETIMEOFFSET()
                FROM AspNetUsers u
                WHERE EXISTS (SELECT 1 FROM Clubs c WHERE c.UserId = u.Id)
                   OR EXISTS (SELECT 1 FROM Coaches c WHERE c.UserId = u.Id)
                   OR EXISTS (SELECT 1 FROM Players p WHERE p.UserId = u.Id)
                   OR EXISTS (SELECT 1 FROM AspNetUserRoles r WHERE r.UserId = u.Id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfirmedAt",
                table: "AspNetUsers");
        }
    }
}
