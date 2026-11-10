using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClubVideoManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "CoachId",
                table: "VideoUploads",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "ClubId",
                table: "VideoUploads",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByCoachId",
                table: "VideoShareLinks",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByClubId",
                table: "VideoShareLinks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "UploadedByCoachId",
                table: "MatchVideos",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "UploadedByClubId",
                table: "MatchVideos",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "squad.managers_only", "es", "Solo el club propietario o el cuerpo técnico vigente pueden realizar esta acción." },
                    { "video.manager_required", "es", "El vídeo debe tener como responsable un club o un técnico." }
                });

            migrationBuilder.CreateIndex(
                name: "IX_VideoUploads_ClubId",
                table: "VideoUploads",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoShareLinks_CreatedByClubId",
                table: "VideoShareLinks",
                column: "CreatedByClubId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchVideos_UploadedByClubId",
                table: "MatchVideos",
                column: "UploadedByClubId");

            migrationBuilder.AddForeignKey(
                name: "FK_MatchVideos_Clubs_UploadedByClubId",
                table: "MatchVideos",
                column: "UploadedByClubId",
                principalTable: "Clubs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VideoShareLinks_Clubs_CreatedByClubId",
                table: "VideoShareLinks",
                column: "CreatedByClubId",
                principalTable: "Clubs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VideoUploads_Clubs_ClubId",
                table: "VideoUploads",
                column: "ClubId",
                principalTable: "Clubs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MatchVideos_Clubs_UploadedByClubId",
                table: "MatchVideos");

            migrationBuilder.DropForeignKey(
                name: "FK_VideoShareLinks_Clubs_CreatedByClubId",
                table: "VideoShareLinks");

            migrationBuilder.DropForeignKey(
                name: "FK_VideoUploads_Clubs_ClubId",
                table: "VideoUploads");

            migrationBuilder.DropIndex(
                name: "IX_VideoUploads_ClubId",
                table: "VideoUploads");

            migrationBuilder.DropIndex(
                name: "IX_VideoShareLinks_CreatedByClubId",
                table: "VideoShareLinks");

            migrationBuilder.DropIndex(
                name: "IX_MatchVideos_UploadedByClubId",
                table: "MatchVideos");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "squad.managers_only", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video.manager_required", "es" });

            migrationBuilder.DropColumn(
                name: "ClubId",
                table: "VideoUploads");

            migrationBuilder.DropColumn(
                name: "CreatedByClubId",
                table: "VideoShareLinks");

            migrationBuilder.DropColumn(
                name: "UploadedByClubId",
                table: "MatchVideos");

            migrationBuilder.AlterColumn<Guid>(
                name: "CoachId",
                table: "VideoUploads",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByCoachId",
                table: "VideoShareLinks",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "UploadedByCoachId",
                table: "MatchVideos",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
