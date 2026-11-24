using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InjuryRecoveryHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "InitialExpectedReturnOn",
                table: "PlayerInjuries",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RelapseOfInjuryId",
                table: "PlayerInjuries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReportedAt",
                table: "PlayerInjuries",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReturnRecordedAt",
                table: "PlayerInjuries",
                type: "datetimeoffset",
                nullable: true);

            // Las lesiones de antes no guardaban cuándo se registraron: se toma el día del parte. La vuelta prevista que
            // tienen es la mejor referencia de la que se previó; y el alta se registró, como tarde, en su último cambio.
            migrationBuilder.Sql(@"
UPDATE PlayerInjuries SET
    ReportedAt = TODATETIMEOFFSET(CAST(StartedOn AS datetime2), '+01:00'),
    InitialExpectedReturnOn = ExpectedReturnOn,
    ReturnRecordedAt = CASE WHEN ReturnedOn IS NOT NULL THEN UpdatedAt END;");

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "injury.has_relapse", "es", "Esa lesión tiene una recaída registrada después del alta: borra la recaída antes de deshacer el alta." },
                    { "injury.private", "es", "Las lesiones de un jugador solo las ven el club, su cuerpo técnico y él mismo." },
                    { "injury.relapse_before_return", "es", "La recaída no puede empezar antes del alta de la lesión anterior." },
                    { "injury.relapse_needs_return", "es", "Solo puede tener una recaída una lesión que ya tiene el alta." },
                    { "injury.return_in_future", "es", "El alta no puede ser posterior a hoy: mientras no la tenga, cambia la vuelta prevista." },
                    { "injury.start_in_future", "es", "La fecha del parte no puede ser posterior a hoy." }
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerInjuries_RelapseOfInjuryId",
                table: "PlayerInjuries",
                column: "RelapseOfInjuryId");

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerInjuries_PlayerInjuries_RelapseOfInjuryId",
                table: "PlayerInjuries",
                column: "RelapseOfInjuryId",
                principalTable: "PlayerInjuries",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlayerInjuries_PlayerInjuries_RelapseOfInjuryId",
                table: "PlayerInjuries");

            migrationBuilder.DropIndex(
                name: "IX_PlayerInjuries_RelapseOfInjuryId",
                table: "PlayerInjuries");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury.has_relapse", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury.private", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury.relapse_before_return", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury.relapse_needs_return", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury.return_in_future", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury.start_in_future", "es" });

            migrationBuilder.DropColumn(
                name: "InitialExpectedReturnOn",
                table: "PlayerInjuries");

            migrationBuilder.DropColumn(
                name: "RelapseOfInjuryId",
                table: "PlayerInjuries");

            migrationBuilder.DropColumn(
                name: "ReportedAt",
                table: "PlayerInjuries");

            migrationBuilder.DropColumn(
                name: "ReturnRecordedAt",
                table: "PlayerInjuries");
        }
    }
}
