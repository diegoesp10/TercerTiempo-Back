using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VideoAnalysisLeaseCancelAndAnalyzerHeartbeat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HeartbeatAt",
                table: "MatchVideoAnalyses",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReadFramesPct",
                table: "MatchVideoAnalyses",
                type: "decimal(5,1)",
                precision: 5,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkerId",
                table: "MatchVideoAnalyses",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VideoAnalyzerHeartbeats",
                columns: table => new
                {
                    WorkerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoAnalyzerHeartbeats", x => x.WorkerId);
                });

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "video_analysis.not_active", "es", "Este vídeo no está en cola ni analizándose: no hay nada que cancelar." }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VideoAnalyzerHeartbeats");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video_analysis.not_active", "es" });

            migrationBuilder.DropColumn(
                name: "HeartbeatAt",
                table: "MatchVideoAnalyses");

            migrationBuilder.DropColumn(
                name: "ReadFramesPct",
                table: "MatchVideoAnalyses");

            migrationBuilder.DropColumn(
                name: "WorkerId",
                table: "MatchVideoAnalyses");
        }
    }
}
