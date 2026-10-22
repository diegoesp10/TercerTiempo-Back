using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VideoVisibilityAndShareLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Visibility",
                table: "MatchVideos",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Private");

            migrationBuilder.CreateTable(
                name: "VideoShareLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VideoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByCoachId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoShareLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VideoShareLinks_Coaches_CreatedByCoachId",
                        column: x => x.CreatedByCoachId,
                        principalTable: "Coaches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VideoShareLinks_MatchVideos_VideoId",
                        column: x => x.VideoId,
                        principalTable: "MatchVideos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "video.share_hours_range", "es", "La validez del enlace tiene que estar entre 1 hora y {0} horas." },
                    { "video.share_invalid", "es", "Este enlace para ver el vídeo no es válido, ha caducado o se ha revocado." },
                    { "video.share_not_found", "es", "El enlace '{0}' no existe." },
                    { "video.visibility_invalid", "es", "La visibilidad tiene que ser Private o Public." }
                });

            migrationBuilder.CreateIndex(
                name: "IX_VideoShareLinks_CreatedByCoachId",
                table: "VideoShareLinks",
                column: "CreatedByCoachId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoShareLinks_Token",
                table: "VideoShareLinks",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VideoShareLinks_VideoId",
                table: "VideoShareLinks",
                column: "VideoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VideoShareLinks");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video.share_hours_range", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video.share_invalid", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video.share_not_found", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video.visibility_invalid", "es" });

            migrationBuilder.DropColumn(
                name: "Visibility",
                table: "MatchVideos");
        }
    }
}
