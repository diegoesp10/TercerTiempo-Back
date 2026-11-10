using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VideoAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Profile_PitchLengthM",
                table: "Teams",
                type: "decimal(5,1)",
                precision: 5,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Profile_PitchWidthM",
                table: "Teams",
                type: "decimal(5,1)",
                precision: 5,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GoalKicksAgainst",
                table: "TeamMatchStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GoalKicksFor",
                table: "TeamMatchStats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "MatchVideoAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VideoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SquadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Progress = table.Column<int>(type: "int", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    OutputPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AnalyzerVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Weights = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ProposalJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Frames = table.Column<int>(type: "int", nullable: true),
                    BallFramesPct = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: true),
                    CalibratedFramesPct = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: true),
                    AnalyzedSeconds = table.Column<int>(type: "int", nullable: true),
                    PitchLengthM = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: true),
                    PitchWidthM = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: true),
                    OurTeam = table.Column<int>(type: "int", nullable: true),
                    AssignmentsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchVideoAnalyses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchVideoAnalyses_MatchVideos_VideoId",
                        column: x => x.VideoId,
                        principalTable: "MatchVideos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchVideoAnalysisCrops",
                columns: table => new
                {
                    AnalysisId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tracklet = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchVideoAnalysisCrops", x => new { x.AnalysisId, x.Tracklet });
                    table.ForeignKey(
                        name: "FK_MatchVideoAnalysisCrops_MatchVideoAnalyses_AnalysisId",
                        column: x => x.AnalysisId,
                        principalTable: "MatchVideoAnalyses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "team.pitch_incomplete", "es", "Indica el largo y el ancho del campo, o deja los dos vacíos." },
                    { "team.pitch_own_teams_only", "es", "Solo puedes cambiar las medidas del campo de los equipos de tu club." },
                    { "video_analysis.already_queued", "es", "Este vídeo ya está en cola o analizándose." },
                    { "video_analysis.crop_not_found", "es", "No hay recorte del jugador seguido {0}." },
                    { "video_analysis.disabled", "es", "El análisis de vídeo no está activado en este servidor." },
                    { "video_analysis.empty_proposal", "es", "El analizador no ha dejado ninguna propuesta." },
                    { "video_analysis.no_video", "es", "Este partido no tiene ningún vídeo de tu plantilla que analizar." },
                    { "video_analysis.not_found", "es", "Este vídeo todavía no se ha analizado." },
                    { "video_analysis.not_pending", "es", "Este análisis no está en cola." },
                    { "video_analysis.not_ready", "es", "El análisis del vídeo todavía no ha terminado." },
                    { "video_analysis.not_running", "es", "Este análisis no está en marcha." },
                    { "video_analysis.nothing_selected", "es", "Marca algún dato de equipo o asigna algún jugador seguido antes de volcar al acta." },
                    { "video_analysis.overwrite", "es", "El acta ya tiene datos distintos en: {0}. Confirma que quieres sobrescribirlos." },
                    { "video_analysis.team_invalid", "es", "Indica cuál de los dos equipos que ha visto el analizador es el tuyo." },
                    { "video_analysis.tracklet_other_team", "es", "El jugador seguido {0} es del otro equipo: no se puede asignar a una ficha de tu plantilla." },
                    { "video_analysis.tracklet_repeated", "es", "El jugador seguido {0} está asignado más de una vez." },
                    { "video_analysis.tracklet_unknown", "es", "El jugador seguido {0} no está en el análisis de este vídeo." },
                    { "video_analysis.video_not_ready", "es", "El vídeo todavía se está comprimiendo: el análisis empieza solo cuando termine." }
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatchVideoAnalyses_Status",
                table: "MatchVideoAnalyses",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MatchVideoAnalyses_VideoId",
                table: "MatchVideoAnalyses",
                column: "VideoId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchVideoAnalysisCrops");

            migrationBuilder.DropTable(
                name: "MatchVideoAnalyses");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "team.pitch_incomplete", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "team.pitch_own_teams_only", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video_analysis.already_queued", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video_analysis.crop_not_found", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video_analysis.disabled", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video_analysis.empty_proposal", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video_analysis.no_video", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video_analysis.not_found", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video_analysis.not_pending", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video_analysis.not_ready", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video_analysis.not_running", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video_analysis.nothing_selected", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video_analysis.overwrite", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video_analysis.team_invalid", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video_analysis.tracklet_other_team", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video_analysis.tracklet_repeated", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video_analysis.tracklet_unknown", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video_analysis.video_not_ready", "es" });

            migrationBuilder.DropColumn(
                name: "Profile_PitchLengthM",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "Profile_PitchWidthM",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "GoalKicksAgainst",
                table: "TeamMatchStats");

            migrationBuilder.DropColumn(
                name: "GoalKicksFor",
                table: "TeamMatchStats");
        }
    }
}
