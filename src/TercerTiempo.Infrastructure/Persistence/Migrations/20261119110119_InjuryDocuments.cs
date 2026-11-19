using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InjuryDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InjuryDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InjuryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SquadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UploadedByCoachId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UploadedByClubId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InjuryDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InjuryDocuments_Clubs_UploadedByClubId",
                        column: x => x.UploadedByClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InjuryDocuments_Coaches_UploadedByCoachId",
                        column: x => x.UploadedByCoachId,
                        principalTable: "Coaches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InjuryDocuments_PlayerInjuries_InjuryId",
                        column: x => x.InjuryId,
                        principalTable: "PlayerInjuries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InjuryDocuments_Squads_SquadId",
                        column: x => x.SquadId,
                        principalTable: "Squads",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "injury_document.format", "es", "Formato no admitido: sube un PDF o una imagen (JPG, PNG o WebP)." },
                    { "injury_document.invalid_kind", "es", "Tipo de documento no válido." },
                    { "injury_document.limit", "es", "Una lesión admite como mucho 10 documentos: borra alguno antes de subir otro." },
                    { "injury_document.not_found", "es", "El documento '{0}' no existe." },
                    { "injury_document.private", "es", "Las pruebas e informes médicos solo los abren el club, el cuerpo técnico y el propio jugador." },
                    { "injury_document.too_large", "es", "Cada documento puede ocupar como mucho 15 MB." }
                });

            migrationBuilder.CreateIndex(
                name: "IX_InjuryDocuments_InjuryId",
                table: "InjuryDocuments",
                column: "InjuryId");

            migrationBuilder.CreateIndex(
                name: "IX_InjuryDocuments_SquadId",
                table: "InjuryDocuments",
                column: "SquadId");

            migrationBuilder.CreateIndex(
                name: "IX_InjuryDocuments_UploadedByClubId",
                table: "InjuryDocuments",
                column: "UploadedByClubId");

            migrationBuilder.CreateIndex(
                name: "IX_InjuryDocuments_UploadedByCoachId",
                table: "InjuryDocuments",
                column: "UploadedByCoachId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InjuryDocuments");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury_document.format", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury_document.invalid_kind", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury_document.limit", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury_document.not_found", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury_document.private", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury_document.too_large", "es" });
        }
    }
}
