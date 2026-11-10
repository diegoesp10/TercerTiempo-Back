using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrainingsAndCallUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Alerts_ClubId_Key",
                table: "Alerts");

            migrationBuilder.AlterColumn<Guid>(
                name: "ClubId",
                table: "Alerts",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "CoachId",
                table: "Alerts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PlayerId",
                table: "Alerts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Exercises",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SquadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Material = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedByCoachId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByClubId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exercises", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Exercises_Clubs_CreatedByClubId",
                        column: x => x.CreatedByClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Exercises_Coaches_CreatedByCoachId",
                        column: x => x.CreatedByCoachId,
                        principalTable: "Coaches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Exercises_Squads_SquadId",
                        column: x => x.SquadId,
                        principalTable: "Squads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchCallUps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SquadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MeetAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    MeetPlace = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedByCoachId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByClubId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchCallUps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchCallUps_Clubs_CreatedByClubId",
                        column: x => x.CreatedByClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchCallUps_Coaches_CreatedByCoachId",
                        column: x => x.CreatedByCoachId,
                        principalTable: "Coaches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchCallUps_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchCallUps_Squads_SquadId",
                        column: x => x.SquadId,
                        principalTable: "Squads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SquadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedByCoachId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByClubId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingSessions_Clubs_CreatedByClubId",
                        column: x => x.CreatedByClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainingSessions_Coaches_CreatedByCoachId",
                        column: x => x.CreatedByCoachId,
                        principalTable: "Coaches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainingSessions_Squads_SquadId",
                        column: x => x.SquadId,
                        principalTable: "Squads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CallUpEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CallUpId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SquadPlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Called = table.Column<bool>(type: "bit", nullable: false),
                    Response = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ResponseNote = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RespondedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NotifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallUpEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CallUpEntries_MatchCallUps_CallUpId",
                        column: x => x.CallUpId,
                        principalTable: "MatchCallUps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CallUpEntries_SquadPlayers_SquadPlayerId",
                        column: x => x.SquadPlayerId,
                        principalTable: "SquadPlayers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TrainingAttendances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SquadPlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingAttendances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingAttendances_SquadPlayers_SquadPlayerId",
                        column: x => x.SquadPlayerId,
                        principalTable: "SquadPlayers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainingAttendances_TrainingSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "TrainingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    ExerciseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingItems_Exercises_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "Exercises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrainingItems_TrainingSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "TrainingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "callup.invalid_response", "es", "Respuesta no válida: el convocado confirma o avisa de que no puede; el que no va se da por enterado." },
                    { "callup.match_not_of_squad", "es", "Ese partido no es de este equipo en esta temporada." },
                    { "callup.match_played", "es", "El partido ya se ha jugado o ya ha empezado: no se puede preparar su convocatoria." },
                    { "callup.match_started", "es", "El partido ya ha empezado: la convocatoria está cerrada." },
                    { "callup.no_players", "es", "Convoca al menos a un jugador antes de publicar." },
                    { "callup.not_called", "es", "No vas convocado a este partido: solo puedes darte por enterado." },
                    { "callup.not_found", "es", "La convocatoria '{0}' no existe." },
                    { "callup.not_published", "es", "La convocatoria todavía no está publicada." },
                    { "exercise.duration_range", "es", "La duración del ejercicio tiene que estar entre 1 y {0} minutos." },
                    { "exercise.invalid_type", "es", "Tipo de ejercicio no válido." },
                    { "exercise.not_found", "es", "El ejercicio '{0}' no existe en la biblioteca de la plantilla." },
                    { "training.absence_past", "es", "El entrenamiento ya ha empezado: avisa directamente a tu cuerpo técnico." },
                    { "training.attendance_invalid", "es", "Estado de asistencia no válido." },
                    { "training.duration_range", "es", "La duración del entrenamiento tiene que estar entre {0} y {1} minutos." },
                    { "training.invalid_kind", "es", "Tipo de entrenamiento no válido." },
                    { "training.items_too_many", "es", "Un entrenamiento admite hasta {0} ejercicios." },
                    { "training.not_found", "es", "El entrenamiento '{0}' no existe." },
                    { "training.repeat_range", "es", "La repetición semanal admite como mucho {0} sesiones: acorta el periodo." },
                    { "training.repeat_weekdays", "es", "Elige al menos un día de la semana para repetir el entrenamiento." }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_ClubId_PlayerId_CoachId_Key",
                table: "Alerts",
                columns: new[] { "ClubId", "PlayerId", "CoachId", "Key" },
                unique: true,
                filter: "[ClubId] IS NOT NULL AND [PlayerId] IS NOT NULL AND [CoachId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_CoachId_CreatedAt",
                table: "Alerts",
                columns: new[] { "CoachId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_PlayerId_CreatedAt",
                table: "Alerts",
                columns: new[] { "PlayerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CallUpEntries_CallUpId_SquadPlayerId",
                table: "CallUpEntries",
                columns: new[] { "CallUpId", "SquadPlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CallUpEntries_SquadPlayerId",
                table: "CallUpEntries",
                column: "SquadPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Exercises_CreatedByClubId",
                table: "Exercises",
                column: "CreatedByClubId");

            migrationBuilder.CreateIndex(
                name: "IX_Exercises_CreatedByCoachId",
                table: "Exercises",
                column: "CreatedByCoachId");

            migrationBuilder.CreateIndex(
                name: "IX_Exercises_SquadId",
                table: "Exercises",
                column: "SquadId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchCallUps_CreatedByClubId",
                table: "MatchCallUps",
                column: "CreatedByClubId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchCallUps_CreatedByCoachId",
                table: "MatchCallUps",
                column: "CreatedByCoachId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchCallUps_MatchId",
                table: "MatchCallUps",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchCallUps_SquadId_MatchId",
                table: "MatchCallUps",
                columns: new[] { "SquadId", "MatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainingAttendances_SessionId_SquadPlayerId",
                table: "TrainingAttendances",
                columns: new[] { "SessionId", "SquadPlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainingAttendances_SquadPlayerId",
                table: "TrainingAttendances",
                column: "SquadPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingItems_ExerciseId",
                table: "TrainingItems",
                column: "ExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingItems_SessionId_Order",
                table: "TrainingItems",
                columns: new[] { "SessionId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainingSessions_CreatedByClubId",
                table: "TrainingSessions",
                column: "CreatedByClubId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingSessions_CreatedByCoachId",
                table: "TrainingSessions",
                column: "CreatedByCoachId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingSessions_SeriesId",
                table: "TrainingSessions",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingSessions_SquadId_StartsAt",
                table: "TrainingSessions",
                columns: new[] { "SquadId", "StartsAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Alerts_Coaches_CoachId",
                table: "Alerts",
                column: "CoachId",
                principalTable: "Coaches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Alerts_Players_PlayerId",
                table: "Alerts",
                column: "PlayerId",
                principalTable: "Players",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Alerts_Coaches_CoachId",
                table: "Alerts");

            migrationBuilder.DropForeignKey(
                name: "FK_Alerts_Players_PlayerId",
                table: "Alerts");

            migrationBuilder.DropTable(
                name: "CallUpEntries");

            migrationBuilder.DropTable(
                name: "TrainingAttendances");

            migrationBuilder.DropTable(
                name: "TrainingItems");

            migrationBuilder.DropTable(
                name: "MatchCallUps");

            migrationBuilder.DropTable(
                name: "Exercises");

            migrationBuilder.DropTable(
                name: "TrainingSessions");

            migrationBuilder.DropIndex(
                name: "IX_Alerts_ClubId_PlayerId_CoachId_Key",
                table: "Alerts");

            migrationBuilder.DropIndex(
                name: "IX_Alerts_CoachId_CreatedAt",
                table: "Alerts");

            migrationBuilder.DropIndex(
                name: "IX_Alerts_PlayerId_CreatedAt",
                table: "Alerts");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "callup.invalid_response", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "callup.match_not_of_squad", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "callup.match_played", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "callup.match_started", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "callup.no_players", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "callup.not_called", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "callup.not_found", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "callup.not_published", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "exercise.duration_range", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "exercise.invalid_type", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "exercise.not_found", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "training.absence_past", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "training.attendance_invalid", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "training.duration_range", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "training.invalid_kind", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "training.items_too_many", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "training.not_found", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "training.repeat_range", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "training.repeat_weekdays", "es" });

            migrationBuilder.DropColumn(
                name: "CoachId",
                table: "Alerts");

            migrationBuilder.DropColumn(
                name: "PlayerId",
                table: "Alerts");

            migrationBuilder.AlterColumn<Guid>(
                name: "ClubId",
                table: "Alerts",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_ClubId_Key",
                table: "Alerts",
                columns: new[] { "ClubId", "Key" },
                unique: true);
        }
    }
}
