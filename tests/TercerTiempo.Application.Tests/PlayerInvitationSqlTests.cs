using Microsoft.EntityFrameworkCore;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Infrastructure.Persistence;

namespace TercerTiempo.Application.Tests;

public class PlayerInvitationSqlTests
{
    [LocalSqlFact]
    public async Task Migrations_and_concurrent_redemptions_preserve_exclusive_player_links()
    {
        // Base creada solo para esta prueba; nunca usa ni borra la base de desarrollo.
        var database = $"TercerTiempo_InviteVerification_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(
            $"Server=localhost;Database={database};Integrated Security=True;TrustServerCertificate=True;Connect Timeout=10").Options;
        await using var setup = new AppDbContext(options);
        try
        {
            await setup.Database.MigrateAsync();
            var now = DateTimeOffset.UtcNow;
            var coach = Coach.Register(Guid.NewGuid(), "Entrenador de prueba", now);
            var player = Player.Register(Guid.NewGuid(), "Pelayo", Position.Forward, null, now);
            var otherAccount = Player.Register(Guid.NewGuid(), "Pelayo", Position.Forward, null, now);
            var competition = Competition.Create("Liga de prueba", "Primera", CompetitionLevel.Regional, 1, null, "Asturias", null);
            var year = SeasonYear.Create("2026/2027", new(2026, 8, 1), new(2027, 6, 30));
            var season = competition.AddSeason("2026/2027", new(2026, 8, 1), new(2027, 6, 30), null, year.Id);
            var teams = Enumerable.Range(1, 3).Select(i => Team.Create($"Equipo {i}", $"E{i}", null)).ToList();
            var squads = teams.Select(t => Squad.Create(t.Id, season.Id, coach.Id, now)).ToList();
            foreach (var squad in squads)
            {
                var entry = squad.AddPlayer("Pelayo", Position.Forward, 9, null, now);
                squad.GeneratePlayerInviteCode(entry.Id);
            }
            setup.AddRange(coach, player, otherAccount, year, competition);
            setup.AddRange(teams);
            setup.AddRange(squads);
            await setup.SaveChangesAsync();

            await using var first = new AppDbContext(options);
            await using var second = new AppDbContext(options);
            var one = await first.Squads.SingleAsync(s => s.Id == squads[0].Id);
            var two = await second.Squads.SingleAsync(s => s.Id == squads[0].Id);
            var code = one.Players.Single().InviteCode!;
            one.LinkPlayer(one.InvitedPlayer(code).Id, player.Id);
            two.LinkPlayer(two.InvitedPlayer(code).Id, otherAccount.Id);
            await first.SaveChangesAsync();
            var reused = await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());
            Assert.Equal(ErrorCode.PlayerInviteChanged, reused.Code);

            await using var third = new AppDbContext(options);
            await using var fourth = new AppDbContext(options);
            var anotherSquad = await third.Squads.SingleAsync(s => s.Id == squads[1].Id);
            var lastSquad = await fourth.Squads.SingleAsync(s => s.Id == squads[2].Id);
            anotherSquad.LinkPlayer(anotherSquad.Players.Single().Id, otherAccount.Id);
            lastSquad.LinkPlayer(lastSquad.Players.Single().Id, otherAccount.Id);
            await third.SaveChangesAsync();
            var duplicateAccount = await Assert.ThrowsAsync<ConflictException>(() => fourth.SaveChangesAsync());
            Assert.Equal(ErrorCode.PlayerAlreadyInSquad, duplicateAccount.Code);

            await using var check = new AppDbContext(options);
            Assert.Equal(2, await check.SquadPlayers.CountAsync(p => p.PlayerId != null));
            Assert.Null((await check.SquadPlayers.SingleAsync(p => p.PlayerId == player.Id)).InviteCode);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }
}

public sealed class LocalSqlFactAttribute : FactAttribute
{
    public LocalSqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("TT_RUN_LOCAL_SQL_TESTS") != "1")
            Skip = "Requiere SQL Server local. Ejecutar con TT_RUN_LOCAL_SQL_TESTS=1; crea y elimina su propia base temporal.";
    }
}
