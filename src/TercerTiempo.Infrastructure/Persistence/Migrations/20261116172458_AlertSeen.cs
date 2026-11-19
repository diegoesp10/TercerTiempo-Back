using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlertSeen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SeenAt",
                table: "Alerts",
                type: "datetimeoffset",
                nullable: true);

            // Lo que ya estaba leído se da por visto: no vuelve a contar en la campanita.
            migrationBuilder.Sql("UPDATE [Alerts] SET [SeenAt] = [ReadAt] WHERE [ReadAt] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SeenAt",
                table: "Alerts");
        }
    }
}
