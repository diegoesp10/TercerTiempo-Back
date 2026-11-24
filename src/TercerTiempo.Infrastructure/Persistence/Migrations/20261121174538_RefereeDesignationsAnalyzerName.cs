using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RefereeDesignationsAnalyzerName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Title",
                table: "VideoUploads");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FromVideoAt",
                table: "TeamMatchStats",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VideoAnalyzer",
                table: "TeamMatchStats",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AnalyzerName",
                table: "MatchVideoAnalyses",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            // Los análisis de antes de que el analizador tuviera nombre los hizo el mismo, en su versión 0.x.
            migrationBuilder.Sql("UPDATE MatchVideoAnalyses SET AnalyzerName = N'Ojeador' WHERE AnalyzerVersion IS NOT NULL;");

            // Datos del equipo volcados del vídeo y sin tocar después: se guardaron en el mismo instante en que el análisis
            // quedó volcado (con las dos partes, vale cualquiera de las dos).
            migrationBuilder.Sql(@"
UPDATE s SET
    VideoAnalyzer = a.AnalyzerName + N' ' + a.AnalyzerVersion,
    FromVideoAt = a.AppliedAt
FROM TeamMatchStats s
JOIN MatchVideos v ON v.SquadId = s.SquadId AND v.MatchId = s.MatchId
JOIN MatchVideoAnalyses a ON a.VideoId = v.Id
WHERE a.AppliedAt = s.UpdatedAt AND a.AnalyzerName IS NOT NULL AND a.AnalyzerVersion IS NOT NULL;");

            migrationBuilder.CreateTable(
                name: "RefereeDesignations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RefereeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefereeDesignations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefereeDesignations_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RefereeDesignations_Referees_RefereeId",
                        column: x => x.RefereeId,
                        principalTable: "Referees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RefereeDesignations_MatchId_Role",
                table: "RefereeDesignations",
                columns: new[] { "MatchId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefereeDesignations_RefereeId",
                table: "RefereeDesignations",
                column: "RefereeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RefereeDesignations");

            migrationBuilder.DropColumn(
                name: "FromVideoAt",
                table: "TeamMatchStats");

            migrationBuilder.DropColumn(
                name: "VideoAnalyzer",
                table: "TeamMatchStats");

            migrationBuilder.DropColumn(
                name: "AnalyzerName",
                table: "MatchVideoAnalyses");

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "VideoUploads",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);
        }
    }
}
