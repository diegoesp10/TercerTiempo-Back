using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TercerTiempo.Infrastructure.Persistence.RealDataMigrations
{
    /// <inheritdoc />
    public partial class CommitteeSanctions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommitteeResolutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Committee = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    MeetingOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Matchday = table.Column<int>(type: "int", nullable: false),
                    MatchdayOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommitteeResolutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommitteeResolutions_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommitteeSanctionChecks",
                columns: table => new
                {
                    SeasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublishedMatchday = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommitteeSanctionChecks", x => x.SeasonId);
                    table.ForeignKey(
                        name: "FK_CommitteeSanctionChecks_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommitteeSanctions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResolutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TeamName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    PersonName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Article = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Matches = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommitteeSanctions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommitteeSanctions_CommitteeResolutions_ResolutionId",
                        column: x => x.ResolutionId,
                        principalTable: "CommitteeResolutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommitteeResolutions_MeetingOn",
                table: "CommitteeResolutions",
                column: "MeetingOn");

            migrationBuilder.CreateIndex(
                name: "IX_CommitteeResolutions_SeasonId_Committee_MeetingOn_Matchday",
                table: "CommitteeResolutions",
                columns: new[] { "SeasonId", "Committee", "MeetingOn", "Matchday" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommitteeSanctions_ResolutionId",
                table: "CommitteeSanctions",
                column: "ResolutionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommitteeSanctionChecks");

            migrationBuilder.DropTable(
                name: "CommitteeSanctions");

            migrationBuilder.DropTable(
                name: "CommitteeResolutions");
        }
    }
}
