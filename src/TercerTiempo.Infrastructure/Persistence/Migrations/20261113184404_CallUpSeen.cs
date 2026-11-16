using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CallUpSeen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SeenAt",
                table: "CallUpEntries",
                type: "datetimeoffset",
                nullable: true);

            // Quien ya firmó la convocatoria, la había visto.
            migrationBuilder.Sql("UPDATE [CallUpEntries] SET [SeenAt] = [RespondedAt] WHERE [RespondedAt] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SeenAt",
                table: "CallUpEntries");
        }
    }
}
