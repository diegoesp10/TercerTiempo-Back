using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VideoProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DurationSeconds",
                table: "MatchVideos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Height",
                table: "MatchVideos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "OriginalSizeBytes",
                table: "MatchVideos",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProcessedAt",
                table: "MatchVideos",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessingAttempts",
                table: "MatchVideos",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ProcessingError",
                table: "MatchVideos",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessingProgress",
                table: "MatchVideos",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProcessingStartedAt",
                table: "MatchVideos",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "MatchVideos",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Uploaded");

            migrationBuilder.AddColumn<byte[]>(
                name: "Version",
                table: "MatchVideos",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "common.concurrent_change", "es", "Alguien ha cambiado esto mientras tanto: vuelve a cargarlo e inténtalo de nuevo." },
                    { "video.not_pending", "es", "Este vídeo no está en cola para comprimir." },
                    { "video.not_processing", "es", "Este vídeo no se está comprimiendo." }
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatchVideos_Status",
                table: "MatchVideos",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MatchVideos_Status",
                table: "MatchVideos");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "common.concurrent_change", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video.not_pending", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video.not_processing", "es" });

            migrationBuilder.DropColumn(
                name: "DurationSeconds",
                table: "MatchVideos");

            migrationBuilder.DropColumn(
                name: "Height",
                table: "MatchVideos");

            migrationBuilder.DropColumn(
                name: "OriginalSizeBytes",
                table: "MatchVideos");

            migrationBuilder.DropColumn(
                name: "ProcessedAt",
                table: "MatchVideos");

            migrationBuilder.DropColumn(
                name: "ProcessingAttempts",
                table: "MatchVideos");

            migrationBuilder.DropColumn(
                name: "ProcessingError",
                table: "MatchVideos");

            migrationBuilder.DropColumn(
                name: "ProcessingProgress",
                table: "MatchVideos");

            migrationBuilder.DropColumn(
                name: "ProcessingStartedAt",
                table: "MatchVideos");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "MatchVideos");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "MatchVideos");
        }
    }
}
