using System.Reflection;
using TercerTiempo.Application.Clubs.Invitations;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Tests;

public class InvitationTeamSearchTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Typing_part_of_a_name_lists_the_teams_with_their_league_club_and_invitation()
    {
        var oviedo = Team.Create("Real Oviedo Vetusta", "Vetusta", null);
        var astur = Team.Create("Astur CF", "Astur", null);
        var covadonga = Team.Create("CD Covadonga", "Covadonga", null);
        var sporting = Team.Create("Real Sporting B", "Sporting B", null);

        var league = Competition.Create("1ª RFEF", "Senior", CompetitionLevel.National, 3, null, "Asturias", null);
        league.MarkCurrent(league.AddSeason("2026/2027", new DateOnly(2026, 8, 1), new DateOnly(2027, 6, 30), null).Id);

        var club = Club.Register(Guid.NewGuid(), "Real Oviedo", Now);
        var linked = ClubInvitation.Create("Real Oviedo Vetusta", null, Now.AddDays(-3), oviedo.Id);
        linked.Redeem(club.Id, Now.AddDays(-1));
        var freeText = ClubInvitation.Create("cd covadonga", null, Now.AddDays(-2));
        var expired = ClubInvitation.Create(null, Now.AddDays(-1), Now.AddDays(-10), astur.Id);

        var handler = new SearchInvitationTeamsHandler(
            Stub<ITeamStore>((method, args) => method switch
            {
                nameof(ITeamStore.GetAllAsync) => Task.FromResult<IReadOnlyList<Team>>([oviedo, astur, covadonga, sporting]),
                nameof(ITeamStore.GetBySeasonAsync) => Task.FromResult<IReadOnlyList<Team>>([oviedo, astur]),
                _ => throw new Exception($"Unexpected call: {method}"),
            }),
            Stub<ICompetitionStore>((method, _) => method == nameof(ICompetitionStore.GetAllAsync)
                ? Task.FromResult<IReadOnlyList<Competition>>([league])
                : throw new Exception($"Unexpected call: {method}")),
            Stub<IClubStore>((method, args) => method switch
            {
                nameof(IClubStore.GetByTeamAsync) => Task.FromResult((Guid)args![0]! == oviedo.Id ? club : null),
                nameof(IClubStore.GetByIdAsync) => Task.FromResult<Club?>(club),
                _ => throw new Exception($"Unexpected call: {method}"),
            }),
            Stub<IClubInvitationStore>((method, _) => method == nameof(IClubInvitationStore.GetAllAsync)
                ? Task.FromResult<IReadOnlyList<ClubInvitation>>([linked, freeText, expired])
                : throw new Exception($"Unexpected call: {method}")),
            new FixedClock(Now));

        Assert.Empty(await handler.Handle(new SearchInvitationTeamsQuery("o"), default));

        var found = await handler.Handle(new SearchInvitationTeamsQuery("ÓVI"), default);
        var row = Assert.Single(found);
        Assert.Equal(("Real Oviedo Vetusta", "1ª RFEF", "Real Oviedo"), (row.Name, row.Competition, row.ClubName));
        Assert.Equal(("used", "Real Oviedo"), (row.Invitation!.Status, row.Invitation.ClubName));

        var withO = await handler.Handle(new SearchInvitationTeamsQuery("co"), default);
        Assert.Equal("CD Covadonga", withO[0].Name);
        Assert.Equal("available", withO[0].Invitation!.Status);

        var astures = Assert.Single(await handler.Handle(new SearchInvitationTeamsQuery("astur"), default));
        Assert.Equal("expired", astures.Invitation!.Status);
        Assert.Null(Assert.Single(await handler.Handle(new SearchInvitationTeamsQuery("sporting"), default)).Invitation);
    }

    private static T Stub<T>(Func<string, object?[]?, object?> call) where T : class
    {
        var result = DispatchProxy.Create<T, PlayerInvitationTests.StoreProxy>();
        ((PlayerInvitationTests.StoreProxy)(object)result).Call = call;
        return result;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
