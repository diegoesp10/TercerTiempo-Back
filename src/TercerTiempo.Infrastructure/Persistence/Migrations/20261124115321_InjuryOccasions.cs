using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InjuryOccasions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MatchId",
                table: "PlayerInjuries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "OccasionDay",
                table: "PlayerInjuries",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OccasionLabel",
                table: "PlayerInjuries",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TrainingSessionId",
                table: "PlayerInjuries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "injury_proposal.occasion", "es", "Una posible lesión de la crónica se registra con su partido como el sitio donde se lesionó." },
                    { "injury.before_occasion", "es", "La fecha del parte no puede ser anterior al día en que se lesionó ({0})." },
                    { "injury.occasion_invalid", "es", "Elige dónde se lesionó: un partido o un entrenamiento, no los dos." },
                    { "injury.occasion_not_found", "es", "Ese partido o entrenamiento no es de esta plantilla o aún no se ha jugado: elige otro de la lista." }
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerInjuries_MatchId",
                table: "PlayerInjuries",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerInjuries_TrainingSessionId",
                table: "PlayerInjuries",
                column: "TrainingSessionId");

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerInjuries_Matches_MatchId",
                table: "PlayerInjuries",
                column: "MatchId",
                principalTable: "Matches",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerInjuries_TrainingSessions_TrainingSessionId",
                table: "PlayerInjuries",
                column: "TrainingSessionId",
                principalTable: "TrainingSessions",
                principalColumn: "Id");

            // Las lesiones registradas desde la crónica de un partido se hicieron en ese partido.
            migrationBuilder.Sql("""
                UPDATE i
                SET i.MatchId = p.MatchId,
                    i.OccasionDay = CAST(m.KickoffAt AT TIME ZONE 'Romance Standard Time' AS date),
                    i.OccasionLabel = LEFT(CONCAT(N'J', m.Matchday, N' · ', h.Name, N' – ', a.Name), 200)
                FROM PlayerInjuries i
                JOIN InjuryProposals p ON p.InjuryId = i.Id AND p.Status = 'Registered'
                JOIN Matches m ON m.Id = p.MatchId
                JOIN Teams h ON h.Id = m.HomeTeamId
                JOIN Teams a ON a.Id = m.AwayTeamId
                WHERE i.MatchId IS NULL AND i.TrainingSessionId IS NULL
                  AND i.StartedOn >= CAST(m.KickoffAt AT TIME ZONE 'Romance Standard Time' AS date);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlayerInjuries_Matches_MatchId",
                table: "PlayerInjuries");

            migrationBuilder.DropForeignKey(
                name: "FK_PlayerInjuries_TrainingSessions_TrainingSessionId",
                table: "PlayerInjuries");

            migrationBuilder.DropIndex(
                name: "IX_PlayerInjuries_MatchId",
                table: "PlayerInjuries");

            migrationBuilder.DropIndex(
                name: "IX_PlayerInjuries_TrainingSessionId",
                table: "PlayerInjuries");

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury_proposal.occasion", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury.before_occasion", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury.occasion_invalid", "es" });

            migrationBuilder.DeleteData(
                table: "ErrorMessages",
                keyColumns: new[] { "Code", "Language" },
                keyValues: new object[] { "injury.occasion_not_found", "es" });

            migrationBuilder.DropColumn(
                name: "MatchId",
                table: "PlayerInjuries");

            migrationBuilder.DropColumn(
                name: "OccasionDay",
                table: "PlayerInjuries");

            migrationBuilder.DropColumn(
                name: "OccasionLabel",
                table: "PlayerInjuries");

            migrationBuilder.DropColumn(
                name: "TrainingSessionId",
                table: "PlayerInjuries");
        }
    }
}
