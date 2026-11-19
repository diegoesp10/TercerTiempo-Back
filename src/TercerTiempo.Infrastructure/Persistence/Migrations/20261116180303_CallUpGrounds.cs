using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CallUpGrounds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MeetGroundId",
                table: "MatchCallUps",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Grounds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Locality = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Latitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    Longitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    LocatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LocateAttempts = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Grounds", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[] { "ground.not_found", "es", "Ese campo no está entre los de la liga. Elige uno de la lista o escribe el sitio a mano." });

            migrationBuilder.CreateIndex(
                name: "IX_MatchCallUps_MeetGroundId",
                table: "MatchCallUps",
                column: "MeetGroundId");

            migrationBuilder.CreateIndex(
                name: "IX_Grounds_Key",
                table: "Grounds",
                column: "Key");

            migrationBuilder.CreateIndex(
                name: "IX_Grounds_Name",
                table: "Grounds",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_MatchCallUps_Grounds_MeetGroundId",
                table: "MatchCallUps",
                column: "MeetGroundId",
                principalTable: "Grounds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MatchCallUps_Grounds_MeetGroundId",
                table: "MatchCallUps");

            migrationBuilder.DropTable(
                name: "Grounds");

            migrationBuilder.DropIndex(
                name: "IX_MatchCallUps_MeetGroundId",
                table: "MatchCallUps");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "ground.not_found", "es" });

            migrationBuilder.DropColumn(
                name: "MeetGroundId",
                table: "MatchCallUps");
        }
    }
}
