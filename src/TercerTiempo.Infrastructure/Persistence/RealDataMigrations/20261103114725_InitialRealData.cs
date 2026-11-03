using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TercerTiempo.Infrastructure.Persistence.RealDataMigrations
{
    /// <inheritdoc />
    public partial class InitialRealData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Cada entorno copia esta base en una foto fija (RealDataCopier) mientras las descargas siguen escribiendo.
            migrationBuilder.Sql("ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON;", suppressTransaction: true);

            migrationBuilder.CreateTable(
                name: "Competitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Level = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Tier = table.Column<int>(type: "int", nullable: false),
                    Group = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Region = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    ExternalProvider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ExternalId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Competitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FederationHttpCache",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    StatusCode = table.Column<int>(type: "int", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FederationHttpCache", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "FederationRequestStates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NextRequestAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PausedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    HourStartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    HourRequests = table.Column<int>(type: "int", nullable: false),
                    DayStartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DayRequests = table.Column<int>(type: "int", nullable: false),
                    BurstRequests = table.Column<int>(type: "int", nullable: false),
                    LastRequestAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SessionEpoch = table.Column<int>(type: "int", nullable: false),
                    PauseKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PauseReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RejectionStreak = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FederationRequestStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FootballSyncSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScheduledScope = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DaysBack = table.Column<int>(type: "int", nullable: false),
                    DaysAhead = table.Column<int>(type: "int", nullable: false),
                    RostersInRound = table.Column<bool>(type: "bit", nullable: false),
                    FullSyncEveryDays = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FootballSyncSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JobRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Job = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Manual = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Errors = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Requests = table.Column<int>(type: "int", nullable: false),
                    ScopeKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ScopeLabel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Environment = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RealDataIngester",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Environment = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Machine = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Since = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    HeartbeatAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RealDataIngester", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Referees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Referees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScrapeRequests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: true),
                    DurationMs = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    JobRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScrapeRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeasonYears",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonYears", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TeamCrestImages",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    SavedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamCrestImages", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ShortName = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ExternalProvider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ExternalId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OfficialProvider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    OfficialId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Profile_OfficialName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    Profile_Locality = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Profile_Province = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Profile_Address = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Profile_PostalCode = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    Profile_Website = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Profile_Ground = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    Profile_GroundSurface = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Profile_GroundAddress = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Profile_GroundLocality = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Profile_ShirtColor = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Profile_ShortsColor = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Profile_SocksColor = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    CrestKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    CrestFromClub = table.Column<bool>(type: "bit", nullable: false),
                    ProfileSyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Seasons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeasonYearId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    ExternalProvider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ExternalId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OfficialProvider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    OfficialId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seasons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Seasons_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Seasons_SeasonYears_SeasonYearId",
                        column: x => x.SeasonYearId,
                        principalTable: "SeasonYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Matches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Matchday = table.Column<int>(type: "int", nullable: false),
                    HomeTeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AwayTeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KickoffAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Venue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExternalProvider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ExternalId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OfficialProvider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    OfficialId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AwayGoals = table.Column<int>(type: "int", nullable: true),
                    HomeGoals = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Matches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Matches_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Matches_Teams_AwayTeamId",
                        column: x => x.AwayTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Matches_Teams_HomeTeamId",
                        column: x => x.HomeTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RosterEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Position = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ShirtNumber = table.Column<int>(type: "int", nullable: true),
                    ExternalProvider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ExternalId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ManagedByClub = table.Column<bool>(type: "bit", nullable: false),
                    Hidden = table.Column<bool>(type: "bit", nullable: false),
                    PhotoKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RosterEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RosterEntries_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RosterEntries_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeasonZones",
                columns: table => new
                {
                    SeasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Color = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonZones", x => new { x.SeasonId, x.Position });
                    table.ForeignKey(
                        name: "FK_SeasonZones_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Standings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Played = table.Column<int>(type: "int", nullable: false),
                    Won = table.Column<int>(type: "int", nullable: false),
                    Drawn = table.Column<int>(type: "int", nullable: false),
                    Lost = table.Column<int>(type: "int", nullable: false),
                    GoalsFor = table.Column<int>(type: "int", nullable: false),
                    GoalsAgainst = table.Column<int>(type: "int", nullable: false),
                    Points = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Standings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Standings_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Standings_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchOfficialLookups",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchOfficialLookups", x => x.MatchId);
                    table.ForeignKey(
                        name: "FK_MatchOfficialLookups_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchOfficials",
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
                    table.PrimaryKey("PK_MatchOfficials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchOfficials_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchOfficials_Referees_RefereeId",
                        column: x => x.RefereeId,
                        principalTable: "Referees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfficialSheets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SourceUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Referee = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    HomeTeamName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    AwayTeamName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    HomeGoals = table.Column<int>(type: "int", nullable: true),
                    AwayGoals = table.Column<int>(type: "int", nullable: true),
                    CheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
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
                name: "SheetDownloads",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SheetDownloads", x => x.MatchId);
                    table.ForeignKey(
                        name: "FK_SheetDownloads_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OfficialSheetEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SheetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Side = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Minute = table.Column<int>(type: "int", nullable: false),
                    ExtraMinute = table.Column<int>(type: "int", nullable: true),
                    PlayerName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    RelatedPlayerName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true)
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SheetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Side = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ShirtNumber = table.Column<int>(type: "int", nullable: true),
                    FullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Started = table.Column<bool>(type: "bit", nullable: false),
                    MinuteIn = table.Column<int>(type: "int", nullable: true),
                    MinuteOut = table.Column<int>(type: "int", nullable: true),
                    MinutesPlayed = table.Column<int>(type: "int", nullable: false),
                    Goals = table.Column<int>(type: "int", nullable: false),
                    PenaltyGoals = table.Column<int>(type: "int", nullable: false),
                    OwnGoals = table.Column<int>(type: "int", nullable: false),
                    YellowCards = table.Column<int>(type: "int", nullable: false),
                    RedCard = table.Column<bool>(type: "bit", nullable: false)
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

            migrationBuilder.InsertData(
                table: "FootballSyncSettings",
                columns: new[] { "Id", "DaysAhead", "DaysBack", "FullSyncEveryDays", "RostersInRound", "ScheduledScope", "UpdatedAt" },
                values: new object[] { new Guid("01a16f00-0000-7000-8000-000000000002"), 4, 3, 7, false, "CurrentRound", new DateTimeOffset(new DateTime(2026, 10, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });

            migrationBuilder.CreateIndex(
                name: "IX_Competitions_ExternalProvider_ExternalId",
                table: "Competitions",
                columns: new[] { "ExternalProvider", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_Competitions_SortOrder",
                table: "Competitions",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_JobRuns_Job_StartedAt",
                table: "JobRuns",
                columns: new[] { "Job", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_AwayTeamId",
                table: "Matches",
                column: "AwayTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_ExternalProvider_ExternalId",
                table: "Matches",
                columns: new[] { "ExternalProvider", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_HomeTeamId",
                table: "Matches",
                column: "HomeTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_OfficialProvider_OfficialId",
                table: "Matches",
                columns: new[] { "OfficialProvider", "OfficialId" });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_SeasonId_AwayTeamId",
                table: "Matches",
                columns: new[] { "SeasonId", "AwayTeamId" });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_SeasonId_HomeTeamId",
                table: "Matches",
                columns: new[] { "SeasonId", "HomeTeamId" });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_SeasonId_KickoffAt",
                table: "Matches",
                columns: new[] { "SeasonId", "KickoffAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_SeasonId_Matchday",
                table: "Matches",
                columns: new[] { "SeasonId", "Matchday" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchOfficials_MatchId_Role",
                table: "MatchOfficials",
                columns: new[] { "MatchId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchOfficials_RefereeId",
                table: "MatchOfficials",
                column: "RefereeId");

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
                name: "IX_Referees_NormalizedName",
                table: "Referees",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Referees_Slug",
                table: "Referees",
                column: "Slug");

            migrationBuilder.CreateIndex(
                name: "IX_RosterEntries_ExternalProvider_ExternalId",
                table: "RosterEntries",
                columns: new[] { "ExternalProvider", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_RosterEntries_SeasonId_NormalizedName",
                table: "RosterEntries",
                columns: new[] { "SeasonId", "NormalizedName" });

            migrationBuilder.CreateIndex(
                name: "IX_RosterEntries_TeamId_SeasonId",
                table: "RosterEntries",
                columns: new[] { "TeamId", "SeasonId" });

            migrationBuilder.CreateIndex(
                name: "IX_ScrapeRequests_At",
                table: "ScrapeRequests",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_ScrapeRequests_JobRunId",
                table: "ScrapeRequests",
                column: "JobRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ScrapeRequests_Provider_At",
                table: "ScrapeRequests",
                columns: new[] { "Provider", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_CompetitionId_Label",
                table: "Seasons",
                columns: new[] { "CompetitionId", "Label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_ExternalProvider_ExternalId",
                table: "Seasons",
                columns: new[] { "ExternalProvider", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_IsCurrent",
                table: "Seasons",
                column: "IsCurrent");

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_OfficialProvider_OfficialId",
                table: "Seasons",
                columns: new[] { "OfficialProvider", "OfficialId" });

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_SeasonYearId_CompetitionId",
                table: "Seasons",
                columns: new[] { "SeasonYearId", "CompetitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeasonYears_IsCurrent",
                table: "SeasonYears",
                column: "IsCurrent",
                unique: true,
                filter: "[IsCurrent] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_SeasonYears_Label",
                table: "SeasonYears",
                column: "Label",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SheetDownloads_Status_Sequence",
                table: "SheetDownloads",
                columns: new[] { "Status", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_Standings_SeasonId_TeamId",
                table: "Standings",
                columns: new[] { "SeasonId", "TeamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Standings_TeamId",
                table: "Standings",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamCrestImages_TeamId",
                table: "TeamCrestImages",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_ExternalProvider_ExternalId",
                table: "Teams",
                columns: new[] { "ExternalProvider", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_Teams_OfficialProvider_OfficialId",
                table: "Teams",
                columns: new[] { "OfficialProvider", "OfficialId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FederationHttpCache");

            migrationBuilder.DropTable(
                name: "FederationRequestStates");

            migrationBuilder.DropTable(
                name: "FootballSyncSettings");

            migrationBuilder.DropTable(
                name: "JobRuns");

            migrationBuilder.DropTable(
                name: "MatchOfficialLookups");

            migrationBuilder.DropTable(
                name: "MatchOfficials");

            migrationBuilder.DropTable(
                name: "OfficialSheetEvents");

            migrationBuilder.DropTable(
                name: "OfficialSheetLines");

            migrationBuilder.DropTable(
                name: "RealDataIngester");

            migrationBuilder.DropTable(
                name: "RosterEntries");

            migrationBuilder.DropTable(
                name: "ScrapeRequests");

            migrationBuilder.DropTable(
                name: "SeasonZones");

            migrationBuilder.DropTable(
                name: "SheetDownloads");

            migrationBuilder.DropTable(
                name: "Standings");

            migrationBuilder.DropTable(
                name: "TeamCrestImages");

            migrationBuilder.DropTable(
                name: "Referees");

            migrationBuilder.DropTable(
                name: "OfficialSheets");

            migrationBuilder.DropTable(
                name: "Matches");

            migrationBuilder.DropTable(
                name: "Seasons");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropTable(
                name: "Competitions");

            migrationBuilder.DropTable(
                name: "SeasonYears");
        }
    }
}
