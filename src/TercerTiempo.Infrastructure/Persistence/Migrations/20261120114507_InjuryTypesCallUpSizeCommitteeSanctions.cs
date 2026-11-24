using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InjuryTypesCallUpSizeCommitteeSanctions : Migration
    {
        private static readonly DateTimeOffset SeededAt = new(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CallUpSize",
                table: "Squads",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Size",
                table: "MatchCallUps",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CommitteeResolutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Committee = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    MeetingOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Matchday = table.Column<int>(type: "int", nullable: false),
                    MatchdayOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommitteeResolutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommitteeResolutions_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InjuryTypes",
                columns: table => new
                {
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    AverageDays = table.Column<int>(type: "int", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InjuryTypes", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "CommitteeSanctions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResolutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TeamName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    PersonName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Article = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Matches = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommitteeSanctions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommitteeSanctions_CommitteeResolutions_ResolutionId",
                        column: x => x.ResolutionId,
                        principalTable: "CommitteeResolutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury.invalid_type", "es" },
                column: "Message",
                value: "Ese tipo de lesión no está en el catálogo: elige otro de la lista.");

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "callup.over_size", "es", "La convocatoria es de {0} jugadores y has marcado a {1}: quita a {2}." },
                    { "callup.size_invalid", "es", "La convocatoria tiene que ser de {0} a {1} jugadores." },
                    { "callup.size_required", "es", "Di a cuántos jugadores convocas para este partido." },
                    { "injury_type.average_days_invalid", "es", "La duración media va de 1 a {0} días; déjala vacía si no hay una referencia." },
                    { "injury_type.duplicate", "es", "Ya hay un tipo de lesión llamado «{0}»." },
                    { "injury_type.in_use", "es", "«{0}» lo tienen ya {1} lesiones guardadas, así que no se puede borrar. Si no quieres que se use más, retíralo: dejará de ofrecerse sin tocar esas lesiones." },
                    { "injury_type.not_found", "es", "El tipo de lesión '{0}' no existe." },
                    { "injury_type.retired", "es", "«{0}» ya no se usa para lesiones nuevas: elige otro tipo." },
                    { "squad.player_in_other_squad", "es", "{0} juega ahora en otra plantilla con su cuenta: no se le puede reincorporar aquí mientras siga allí." },
                    { "squad.player_left_before", "es", "{0} causó baja en la plantilla el {1}: no puede salir en lo que pasó después." },
                    { "squad.player_left_exists", "es", "{0} está en el grupo «De baja» de la plantilla: reincorpóralo desde ahí en vez de añadirlo otra vez (así conserva su historial)." },
                    { "squad.player_left_read_only", "es", "{0} ya no está en la plantilla: lo suyo se puede consultar, pero no cambiar. Reincorpóralo si vuelve." },
                    { "squad.player_not_left", "es", "{0} no está de baja: sigue en la plantilla." }
                });

            // El catálogo nace con los tipos que antes eran fijos en el código: las lesiones ya guardadas llevan sus códigos.
            // Después lo mantiene el administrador, así que no es HasData (una migración futura no debe tocarlo).
            migrationBuilder.InsertData(
                table: "InjuryTypes",
                columns: new[] { "Code", "Label", "AverageDays", "SortOrder", "Active", "UpdatedAt" },
                values: new object[,]
                {
                    { "MuscleContracture", "Contractura muscular", 7, 1, true, SeededAt },
                    { "MuscleTear", "Rotura fibrilar", 21, 2, true, SeededAt },
                    { "AnkleSprain", "Esguince de tobillo", 21, 3, true, SeededAt },
                    { "KneeSprain", "Esguince de rodilla", 28, 4, true, SeededAt },
                    { "CruciateLigament", "Ligamento cruzado", 240, 5, true, SeededAt },
                    { "Meniscus", "Menisco", 60, 6, true, SeededAt },
                    { "Fracture", "Fractura", 60, 7, true, SeededAt },
                    { "Contusion", "Contusión / golpe", 5, 8, true, SeededAt },
                    { "Tendinitis", "Tendinitis", 21, 9, true, SeededAt },
                    { "Pubalgia", "Pubalgia", 42, 10, true, SeededAt },
                    { "Concussion", "Conmoción", 10, 11, true, SeededAt },
                    { "Illness", "Enfermedad", 5, 12, true, SeededAt },
                    { "Other", "Otra", null, 99, true, SeededAt },
                });

            // Un código que no esté en el catálogo (no debería haberlo) pasa a «Otra» para poder crear la clave ajena.
            migrationBuilder.Sql("UPDATE PlayerInjuries SET Type = 'Other' WHERE Type NOT IN (SELECT Code FROM InjuryTypes);");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerInjuries_Type",
                table: "PlayerInjuries",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_CommitteeResolutions_MeetingOn",
                table: "CommitteeResolutions",
                column: "MeetingOn");

            migrationBuilder.CreateIndex(
                name: "IX_CommitteeResolutions_SeasonId_Committee_MeetingOn_Matchday",
                table: "CommitteeResolutions",
                columns: new[] { "SeasonId", "Committee", "MeetingOn", "Matchday" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommitteeSanctions_ResolutionId",
                table: "CommitteeSanctions",
                column: "ResolutionId");

            migrationBuilder.CreateIndex(
                name: "IX_InjuryTypes_Label",
                table: "InjuryTypes",
                column: "Label",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerInjuries_InjuryTypes_Type",
                table: "PlayerInjuries",
                column: "Type",
                principalTable: "InjuryTypes",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlayerInjuries_InjuryTypes_Type",
                table: "PlayerInjuries");

            migrationBuilder.DropTable(
                name: "CommitteeSanctions");

            migrationBuilder.DropTable(
                name: "InjuryTypes");

            migrationBuilder.DropTable(
                name: "CommitteeResolutions");

            migrationBuilder.DropIndex(
                name: "IX_PlayerInjuries_Type",
                table: "PlayerInjuries");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "callup.over_size", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "callup.size_invalid", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "callup.size_required", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury_type.average_days_invalid", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury_type.duplicate", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury_type.in_use", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury_type.not_found", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury_type.retired", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "squad.player_in_other_squad", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "squad.player_left_before", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "squad.player_left_exists", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "squad.player_left_read_only", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "squad.player_not_left", "es" });

            migrationBuilder.DropColumn(
                name: "CallUpSize",
                table: "Squads");

            migrationBuilder.DropColumn(
                name: "Size",
                table: "MatchCallUps");

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury.invalid_type", "es" },
                column: "Message",
                value: "Tipo de lesión no válido.");
        }
    }
}
