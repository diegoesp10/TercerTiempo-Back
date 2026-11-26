using Microsoft.EntityFrameworkCore;
using TercerTiempo.Application.Trainings;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Trainings;
using TercerTiempo.Infrastructure.Persistence;
using TercerTiempo.Infrastructure.Persistence.Stores;

namespace TercerTiempo.Application.Tests;

// La puerta de licencia y las huellas leen el permiso y la convocatoria con proyecciones (EXISTS) en vez de con las
// entidades: tienen que dar lo mismo que las reglas del dominio y que la convocatoria entera.
public class SquadAccessSqlTests
{
    [LocalSqlFact]
    public async Task Projected_access_and_call_up_stamp_match_the_entities()
    {
        // Base creada solo para esta prueba; nunca usa ni borra la base de desarrollo.
        var database = $"TercerTiempo_SquadAccess_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(
            $"Server=localhost;Database={database};Integrated Security=True;TrustServerCertificate=True;Connect Timeout=10").Options;
        await using var setup = new AppDbContext(options);
        try
        {
            await setup.Database.MigrateAsync();
            var now = DateTimeOffset.UtcNow;
            var today = Squad.Today(now);
            var competition = Competition.Create("Liga de prueba", "Primera", CompetitionLevel.Regional, 1, null, "Asturias", null);
            var year = SeasonYear.Create("2026/2027", new(2026, 8, 1), new(2027, 6, 30));
            var season = competition.AddSeason("2026/2027", new(2026, 8, 1), new(2027, 6, 30), null, year.Id);
            var team = Team.Create("Equipo", "EQ", null);
            var rival = Team.Create("Rival", "RI", null);
            var head = Coach.Register(Guid.NewGuid(), "Primer entrenador", now);
            var expired = Coach.Register(Guid.NewGuid(), "Nombramiento caducado", now);
            var gone = Coach.Register(Guid.NewGuid(), "Se fue", now);
            var stranger = Coach.Register(Guid.NewGuid(), "De otro equipo", now);
            var pelayo = Player.Register(Guid.NewGuid(), "Pelayo", Position.Forward, null, now);
            var nel = Player.Register(Guid.NewGuid(), "Nel", Position.Defender, null, now);
            var club = Club.Register(Guid.NewGuid(), "Club del equipo", now);
            club.AddTeam(team.Id, now);
            var otherClub = Club.Register(Guid.NewGuid(), "Otro club", now);

            var squad = Squad.Create(team.Id, season.Id, head.Id, now);
            squad.AddStaff(expired.Id, StaffRole.AssistantCoach, now, validUntil: today.AddDays(-1));
            squad.AddStaff(gone.Id, StaffRole.Delegate, now);
            squad.RemoveStaff(gone.Id, now);
            var pelayoEntry = squad.LinkPlayer(squad.AddPlayer("Pelayo", Position.Forward, null, null, now).Id, pelayo.Id);
            var nelEntry = squad.LinkPlayer(squad.AddPlayer("Nel", Position.Defender, null, null, now).Id, nel.Id);
            squad.RemovePlayer(nelEntry.Id, now);

            var match = Match.Schedule(season.Id, 1, team.Id, rival.Id, now.AddDays(3), null, null);
            var callUp = MatchCallUp.Draft(squad, match, SquadEditor.Coach(head.Id), now);
            callUp.Arrange(squad, 7, new HashSet<Guid> { pelayoEntry.Id }, now.AddDays(3).AddHours(-1), null, "Campo de entrenamiento", null, now);
            callUp.Publish(now);
            callUp.Entries.Single(e => e.SquadPlayerId == pelayoEntry.Id).MarkNotified(now);
            callUp.MarkSeen(pelayoEntry.Id, now.AddMinutes(5));

            setup.AddRange(year, competition, team, rival, head, expired, gone, stranger, pelayo, nel, club, otherClub, squad, match, callUp);
            await setup.SaveChangesAsync();

            await using var db = new AppDbContext(options);
            var squads = new SquadStore(db);
            async Task<SquadAccessRow> Access(Guid? coachId = null, Guid? playerId = null, Guid? clubId = null) =>
                (await squads.GetAccessAsync(squad.Id, coachId, playerId, clubId, today))!;

            foreach (var coach in new[] { head, expired, gone, stranger })
                Assert.Equal(squad.IsStaff(coach.Id, today), (await Access(coachId: coach.Id)).IsStaff);
            foreach (var player in new[] { pelayo, nel })
                Assert.Equal(squad.IsMember(player.Id), (await Access(playerId: player.Id)).IsMember);
            foreach (var someClub in new[] { club, otherClub })
                Assert.Equal(someClub.Manages(team.Id), (await Access(clubId: someClub.Id)).IsClub);

            Assert.True((await Access(coachId: head.Id)).IsManager);
            Assert.False((await Access(coachId: expired.Id)).IsInsider);
            Assert.True((await Access(playerId: pelayo.Id)).IsInsider);
            Assert.False((await Access(playerId: pelayo.Id)).IsManager);
            Assert.Null(await squads.GetAccessAsync(Guid.NewGuid(), head.Id, null, null, today));

            var stored = await new CallUpStore(db).GetStampRowAsync(squad.Id, match.Id);
            Assert.NotNull(stored);
            Assert.Equal(CallUpStamp.Of(callUp, closed: false), CallUpStamp.Of(stored, closed: false));
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }
}
