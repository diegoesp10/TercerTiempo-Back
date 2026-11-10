using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MatchPasses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VideoPurchases");

            migrationBuilder.DropTable(
                name: "VideoShareLinks");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video.already_purchased", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video.owner_access", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video.payment_failed", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video.payment_rejected", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video.share_hours_range", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video.share_invalid", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video.share_not_found", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video.visibility_invalid", "es" });

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "MatchVideos");

            migrationBuilder.DropColumn(
                name: "PriceCents",
                table: "MatchVideos");

            migrationBuilder.DropColumn(
                name: "Visibility",
                table: "MatchVideos");

            migrationBuilder.AddColumn<int>(
                name: "SheetPassPriceCents",
                table: "Clubs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VideoPassPriceCents",
                table: "Clubs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "MatchPasses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SquadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClubId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    IncludesSheet = table.Column<bool>(type: "bit", nullable: false),
                    IncludesVideo = table.Column<bool>(type: "bit", nullable: false),
                    AllowsDownload = table.Column<bool>(type: "bit", nullable: false),
                    PriceCents = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Recipient = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ForUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByCoachId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByClubId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RedeemedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RedeemedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    RedeemedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchPasses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchPasses_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchPasses_Clubs_CreatedByClubId",
                        column: x => x.CreatedByClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchPasses_Coaches_CreatedByCoachId",
                        column: x => x.CreatedByCoachId,
                        principalTable: "Coaches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchPasses_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchPasses_Squads_SquadId",
                        column: x => x.SquadId,
                        principalTable: "Squads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchPasses_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MatchPassPrices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClubId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SquadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SheetPriceCents = table.Column<int>(type: "int", nullable: true),
                    VideoPriceCents = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchPassPrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchPassPrices_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchPassPrices_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchPassPrices_Squads_SquadId",
                        column: x => x.SquadId,
                        principalTable: "Squads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchPassRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SquadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClubId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequesterUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequesterName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    WantsSheet = table.Column<bool>(type: "bit", nullable: false),
                    WantsVideo = table.Column<bool>(type: "bit", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PassId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchPassRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchPassRequests_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchPassRequests_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchPassRequests_Squads_SquadId",
                        column: x => x.SquadId,
                        principalTable: "Squads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchPassRequests_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PassSales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PassId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClubId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuyerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AmountCents = table.Column<int>(type: "int", nullable: false),
                    ClubSharePercent = table.Column<int>(type: "int", nullable: false),
                    ClubShareCents = table.Column<int>(type: "int", nullable: false),
                    PlatformShareCents = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PaymentReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PassSales", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "squad.private", "es" },
                column: "Message",
                value: "Los datos de esta plantilla son privados: solo su club, su cuerpo técnico y sus jugadores pueden verlos.");

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video.locked", "es" },
                column: "Message",
                value: "Este vídeo es privado: pide acceso al club o canjea un pase de este partido.");

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video.not_allowed", "es" },
                column: "Message",
                value: "Solo el club, el cuerpo técnico y los jugadores de la plantilla, y quien tenga un pase de este partido, pueden ver este vídeo.");

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "pass_request.closed", "es", "Esta solicitud ya está resuelta." },
                    { "pass_request.duplicate", "es", "Ya tienes una solicitud pendiente de este partido." },
                    { "pass_request.not_found", "es", "La solicitud '{0}' no existe." },
                    { "pass_request.squad_private", "es", "Esta plantilla no acepta solicitudes: su acta interna es privada." },
                    { "pass.already_redeemed", "es", "Este pase ya lo ha canjeado otra persona." },
                    { "pass.already_yours", "es", "Ya canjeaste este pase: lo tienes en «Mis pases»." },
                    { "pass.download_needs_video", "es", "La descarga solo se puede dar junto con el vídeo." },
                    { "pass.empty", "es", "El pase tiene que incluir el acta interna, el vídeo o los dos." },
                    { "pass.expired", "es", "Este pase caducó sin canjearse: pide otro al club." },
                    { "pass.match_not_played", "es", "Solo hay pases de partidos ya jugados." },
                    { "pass.no_club", "es", "Este equipo no tiene club en Tercer Tiempo: sin club no hay pases ni solicitudes." },
                    { "pass.no_video", "es", "La plantilla no tiene vídeo de este partido: el pase no puede incluirlo." },
                    { "pass.not_for_you", "es", "Este pase es para la persona que lo solicitó." },
                    { "pass.not_found", "es", "Ese código de pase no existe: revisa que esté bien escrito." },
                    { "pass.own_team", "es", "Ya ves todo lo interno de este partido: es de tu equipo." },
                    { "pass.paid_cannot_revoke", "es", "Este pase ya está pagado: quien lo compró conserva el acceso." },
                    { "pass.payment_failed", "es", "El pago no se ha completado." },
                    { "pass.payment_rejected", "es", "El pago no se ha completado: {0}" },
                    { "pass.revoked", "es", "El club ha retirado este pase." },
                    { "video.download_denied", "es", "Tu acceso a este vídeo no incluye la descarga del archivo." },
                    { "video.storage_full", "es", "No queda espacio para guardar un vídeo de {0}: avisa al administrador." }
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatchPasses_ClubId",
                table: "MatchPasses",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchPasses_Code",
                table: "MatchPasses",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchPasses_CreatedByClubId",
                table: "MatchPasses",
                column: "CreatedByClubId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchPasses_CreatedByCoachId",
                table: "MatchPasses",
                column: "CreatedByCoachId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchPasses_ForUserId",
                table: "MatchPasses",
                column: "ForUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchPasses_MatchId",
                table: "MatchPasses",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchPasses_RedeemedByUserId",
                table: "MatchPasses",
                column: "RedeemedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchPasses_SquadId_MatchId",
                table: "MatchPasses",
                columns: new[] { "SquadId", "MatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchPasses_TeamId",
                table: "MatchPasses",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchPassPrices_ClubId",
                table: "MatchPassPrices",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchPassPrices_MatchId",
                table: "MatchPassPrices",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchPassPrices_SquadId_MatchId",
                table: "MatchPassPrices",
                columns: new[] { "SquadId", "MatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchPassRequests_ClubId",
                table: "MatchPassRequests",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchPassRequests_MatchId",
                table: "MatchPassRequests",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchPassRequests_RequesterUserId",
                table: "MatchPassRequests",
                column: "RequesterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchPassRequests_SquadId",
                table: "MatchPassRequests",
                column: "SquadId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchPassRequests_TeamId",
                table: "MatchPassRequests",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_PassSales_ClubId",
                table: "PassSales",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_PassSales_PassId",
                table: "PassSales",
                column: "PassId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchPasses");

            migrationBuilder.DropTable(
                name: "MatchPassPrices");

            migrationBuilder.DropTable(
                name: "MatchPassRequests");

            migrationBuilder.DropTable(
                name: "PassSales");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "pass_request.closed", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "pass_request.duplicate", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "pass_request.not_found", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "pass_request.squad_private", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "pass.already_redeemed", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "pass.already_yours", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "pass.download_needs_video", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "pass.empty", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "pass.expired", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "pass.match_not_played", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "pass.no_club", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "pass.no_video", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "pass.not_for_you", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "pass.not_found", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "pass.own_team", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "pass.paid_cannot_revoke", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "pass.payment_failed", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "pass.payment_rejected", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "pass.revoked", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video.download_denied", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video.storage_full", "es" });

            migrationBuilder.DropColumn(
                name: "SheetPassPriceCents",
                table: "Clubs");

            migrationBuilder.DropColumn(
                name: "VideoPassPriceCents",
                table: "Clubs");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "MatchVideos",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "PriceCents",
                table: "MatchVideos",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Visibility",
                table: "MatchVideos",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "VideoPurchases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AmountCents = table.Column<int>(type: "int", nullable: false),
                    CoachId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    PaymentReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PurchasedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    VideoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
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
                name: "VideoShareLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByClubId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByCoachId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Token = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    VideoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoShareLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VideoShareLinks_Clubs_CreatedByClubId",
                        column: x => x.CreatedByClubId,
                        principalTable: "Clubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VideoShareLinks_Coaches_CreatedByCoachId",
                        column: x => x.CreatedByCoachId,
                        principalTable: "Coaches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VideoShareLinks_MatchVideos_VideoId",
                        column: x => x.VideoId,
                        principalTable: "MatchVideos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "squad.private", "es" },
                column: "Message",
                value: "Los datos de esta plantilla son privados: solo su cuerpo técnico y sus jugadores pueden verlos.");

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video.locked", "es" },
                column: "Message",
                value: "Este vídeo es de pago: compra el acceso para verlo.");

            migrationBuilder.UpdateData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "video.not_allowed", "es" },
                column: "Message",
                value: "Solo la plantilla que subió el vídeo y los técnicos que lo han comprado pueden verlo.");

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "video.already_purchased", "es", "Ya tienes acceso a este vídeo." },
                    { "video.owner_access", "es", "Este vídeo es de tu plantilla: ya puedes verlo." },
                    { "video.payment_failed", "es", "El pago no se ha completado." },
                    { "video.payment_rejected", "es", "El pago no se ha completado: {0}" },
                    { "video.share_hours_range", "es", "La validez del enlace tiene que estar entre 1 hora y {0} horas." },
                    { "video.share_invalid", "es", "Este enlace para ver el vídeo no es válido, ha caducado o se ha revocado." },
                    { "video.share_not_found", "es", "El enlace '{0}' no existe." },
                    { "video.visibility_invalid", "es", "La visibilidad tiene que ser Private o Public." }
                });

            migrationBuilder.CreateIndex(
                name: "IX_VideoPurchases_CoachId",
                table: "VideoPurchases",
                column: "CoachId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoPurchases_VideoId_CoachId",
                table: "VideoPurchases",
                columns: new[] { "VideoId", "CoachId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VideoShareLinks_CreatedByClubId",
                table: "VideoShareLinks",
                column: "CreatedByClubId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoShareLinks_CreatedByCoachId",
                table: "VideoShareLinks",
                column: "CreatedByCoachId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoShareLinks_Token",
                table: "VideoShareLinks",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VideoShareLinks_VideoId",
                table: "VideoShareLinks",
                column: "VideoId");
        }
    }
}
