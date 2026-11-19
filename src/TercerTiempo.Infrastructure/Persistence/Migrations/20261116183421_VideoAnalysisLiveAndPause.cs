using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VideoAnalysisLiveAndPause : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LiveAt",
                table: "MatchVideoAnalyses",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LiveJson",
                table: "MatchVideoAnalyses",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogJson",
                table: "MatchVideoAnalyses",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PausedAt",
                table: "MatchVideoAnalyses",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PausedSeconds",
                table: "MatchVideoAnalyses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Stage",
                table: "MatchVideoAnalyses",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "video_analysis.not_pausable", "es", "Solo se puede pausar un análisis que esté en cola o analizándose." },
                    { "video_analysis.not_paused", "es", "Este análisis no está en pausa: no hay nada que reanudar." }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video_analysis.not_pausable", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video_analysis.not_paused", "es" });

            migrationBuilder.DropColumn(
                name: "LiveAt",
                table: "MatchVideoAnalyses");

            migrationBuilder.DropColumn(
                name: "LiveJson",
                table: "MatchVideoAnalyses");

            migrationBuilder.DropColumn(
                name: "LogJson",
                table: "MatchVideoAnalyses");

            migrationBuilder.DropColumn(
                name: "PausedAt",
                table: "MatchVideoAnalyses");

            migrationBuilder.DropColumn(
                name: "PausedSeconds",
                table: "MatchVideoAnalyses");

            migrationBuilder.DropColumn(
                name: "Stage",
                table: "MatchVideoAnalyses");
        }
    }
}
