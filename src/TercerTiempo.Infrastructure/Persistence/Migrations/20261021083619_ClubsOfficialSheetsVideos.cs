using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClubsOfficialSheetsVideos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OfficialId",
                table: "Teams",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OfficialProvider",
                table: "Teams",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AppointmentId",
                table: "SquadStaff",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ValidUntil",
                table: "SquadStaff",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OfficialId",
                table: "Seasons",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OfficialProvider",
                table: "Seasons",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OfficialId",
                table: "Matches",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OfficialProvider",
                table: "Matches",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Clubs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clubs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MatchVideos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SquadId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TeamId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeasonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UploadedByCoachId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    StorageKey = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    PriceCents = table.Column<int>(type: "INTEGER", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchVideos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchVideos_Coaches_UploadedByCoachId",
                        column: x => x.UploadedByCoachId,
                        principalTable: "Coaches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchVideos_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchVideos_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchVideos_Squads_SquadId",
                        column: x => x.SquadId,
                        principalTable: "Squads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchVideos_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfficialSheets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Referee = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    HomeTeamName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    AwayTeamName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    HomeGoals = table.Column<int>(type: "INTEGER", nullable: true),
                    AwayGoals = table.Column<int>(type: "INTEGER", nullable: true),
                    CheckedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficialSheets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfficialSheets_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClubTeams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClubId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TeamId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubTeams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClubTeams_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClubTeams_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CoachAppointments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClubId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TeamId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeasonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CoachName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    NormalizedCoachName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Role = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AccessCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CoachId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RedeemedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ValidFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoachAppointments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoachAppointments_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CoachAppointments_Coaches_CoachId",
                        column: x => x.CoachId,
                        principalTable: "Coaches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CoachAppointments_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CoachAppointments_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VideoPurchases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CoachId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AmountCents = table.Column<int>(type: "INTEGER", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    PaymentReference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PurchasedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoPurchases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VideoPurchases_Coaches_CoachId",
                        column: x => x.CoachId,
                        principalTable: "Coaches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VideoPurchases_MatchVideos_VideoId",
                        column: x => x.VideoId,
                        principalTable: "MatchVideos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OfficialSheetEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SheetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    Side = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Minute = table.Column<int>(type: "INTEGER", nullable: false),
                    ExtraMinute = table.Column<int>(type: "INTEGER", nullable: true),
                    PlayerName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    RelatedPlayerName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficialSheetEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfficialSheetEvents_OfficialSheets_SheetId",
                        column: x => x.SheetId,
                        principalTable: "OfficialSheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OfficialSheetLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SheetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Side = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ShirtNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    FullName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Started = table.Column<bool>(type: "INTEGER", nullable: false),
                    MinuteIn = table.Column<int>(type: "INTEGER", nullable: true),
                    MinuteOut = table.Column<int>(type: "INTEGER", nullable: true),
                    MinutesPlayed = table.Column<int>(type: "INTEGER", nullable: false),
                    Goals = table.Column<int>(type: "INTEGER", nullable: false),
                    PenaltyGoals = table.Column<int>(type: "INTEGER", nullable: false),
                    OwnGoals = table.Column<int>(type: "INTEGER", nullable: false),
                    YellowCards = table.Column<int>(type: "INTEGER", nullable: false),
                    RedCard = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficialSheetLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfficialSheetLines_OfficialSheets_SheetId",
                        column: x => x.SheetId,
                        principalTable: "OfficialSheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Teams_OfficialProvider_OfficialId",
                table: "Teams",
                columns: new[] { "OfficialProvider", "OfficialId" });

            migrationBuilder.CreateIndex(
                name: "IX_SquadStaff_AppointmentId",
                table: "SquadStaff",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_OfficialProvider_OfficialId",
                table: "Seasons",
                columns: new[] { "OfficialProvider", "OfficialId" });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_OfficialProvider_OfficialId",
                table: "Matches",
                columns: new[] { "OfficialProvider", "OfficialId" });

            migrationBuilder.CreateIndex(
                name: "IX_Clubs_UserId",
                table: "Clubs",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClubTeams_ClubId",
                table: "ClubTeams",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubTeams_TeamId",
                table: "ClubTeams",
                column: "TeamId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CoachAppointments_AccessCode",
                table: "CoachAppointments",
                column: "AccessCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CoachAppointments_ClubId",
                table: "CoachAppointments",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_CoachAppointments_CoachId",
                table: "CoachAppointments",
                column: "CoachId");

            migrationBuilder.CreateIndex(
                name: "IX_CoachAppointments_SeasonId",
                table: "CoachAppointments",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_CoachAppointments_TeamId_SeasonId",
                table: "CoachAppointments",
                columns: new[] { "TeamId", "SeasonId" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchVideos_MatchId",
                table: "MatchVideos",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchVideos_SeasonId",
                table: "MatchVideos",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchVideos_SquadId",
                table: "MatchVideos",
                column: "SquadId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchVideos_TeamId",
                table: "MatchVideos",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchVideos_UploadedByCoachId",
                table: "MatchVideos",
                column: "UploadedByCoachId");

            migrationBuilder.CreateIndex(
                name: "IX_OfficialSheetEvents_SheetId",
                table: "OfficialSheetEvents",
                column: "SheetId");

            migrationBuilder.CreateIndex(
                name: "IX_OfficialSheetLines_SheetId",
                table: "OfficialSheetLines",
                column: "SheetId");

            migrationBuilder.CreateIndex(
                name: "IX_OfficialSheets_MatchId",
                table: "OfficialSheets",
                column: "MatchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VideoPurchases_CoachId",
                table: "VideoPurchases",
                column: "CoachId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoPurchases_VideoId_CoachId",
                table: "VideoPurchases",
                columns: new[] { "VideoId", "CoachId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SquadStaff_CoachAppointments_AppointmentId",
                table: "SquadStaff",
                column: "AppointmentId",
                principalTable: "CoachAppointments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SquadStaff_CoachAppointments_AppointmentId",
                table: "SquadStaff");

            migrationBuilder.DropTable(
                name: "ClubTeams");

            migrationBuilder.DropTable(
                name: "CoachAppointments");

            migrationBuilder.DropTable(
                name: "OfficialSheetEvents");

            migrationBuilder.DropTable(
                name: "OfficialSheetLines");

            migrationBuilder.DropTable(
                name: "VideoPurchases");

            migrationBuilder.DropTable(
                name: "Clubs");

            migrationBuilder.DropTable(
                name: "OfficialSheets");

            migrationBuilder.DropTable(
                name: "MatchVideos");

            migrationBuilder.DropIndex(
                name: "IX_Teams_OfficialProvider_OfficialId",
                table: "Teams");

            migrationBuilder.DropIndex(
                name: "IX_SquadStaff_AppointmentId",
                table: "SquadStaff");

            migrationBuilder.DropIndex(
                name: "IX_Seasons_OfficialProvider_OfficialId",
                table: "Seasons");

            migrationBuilder.DropIndex(
                name: "IX_Matches_OfficialProvider_OfficialId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "OfficialId",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "OfficialProvider",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "AppointmentId",
                table: "SquadStaff");

            migrationBuilder.DropColumn(
                name: "ValidUntil",
                table: "SquadStaff");

            migrationBuilder.DropColumn(
                name: "OfficialId",
                table: "Seasons");

            migrationBuilder.DropColumn(
                name: "OfficialProvider",
                table: "Seasons");

            migrationBuilder.DropColumn(
                name: "OfficialId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "OfficialProvider",
                table: "Matches");
        }
    }
}
