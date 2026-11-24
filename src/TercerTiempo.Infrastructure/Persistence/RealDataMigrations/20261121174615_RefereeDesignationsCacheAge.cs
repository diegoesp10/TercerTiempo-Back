using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TercerTiempo.Infrastructure.Persistence.RealDataMigrations
{
    /// <inheritdoc />
    public partial class RefereeDesignationsCacheAge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FetchedAt",
                table: "FederationHttpCache",
                type: "datetimeoffset",
                nullable: true);

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
                name: "FetchedAt",
                table: "FederationHttpCache");
        }
    }
}
