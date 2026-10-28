using System.Reflection;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Clubs.Roster;
using TercerTiempo.Application.Players.Card;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Tests;

// Ficha de un jugador: la básica para cualquier cuenta con sesión; la completa para su club y su plantilla o, si el
// jugador la hace pública, para todos.
public class PlayerCardTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private readonly Team _team = Team.Create("CD Vallobín", "VAL", null);
    private readonly Competition _competition = Competition.Create("1ª AsturFútbol", "Primera", CompetitionLevel.Regional, 5, null, "Asturias", null);
    private readonly Season _season;
    private readonly RosterEntry _entry;
    private readonly Squad _squad;
    private readonly Player _account = Player.Register(Guid.NewGuid(), "Mateo García Martínez", Position.Forward, null, Now);
    private readonly Guid _coachId = Guid.NewGuid();

    public PlayerCardTests()
    {
        _season = _competition.AddSeason("2026/2027", new(2026, 8, 1), new(2027, 6, 30), null);
        _competition.MarkCurrent(_season.Id);
        _entry = RosterEntry.Create(_team.Id, _season.Id, "Mateo García Martínez", Position.Forward, 10, null, Now);
        _squad = Squad.Create(_team.Id, _season.Id, _coachId, Now);
        var player = _squad.AddPlayer(_entry.FullName, Position.Forward, 10, _entry.Id, Now);
        _squad.LinkPlayer(player.Id, _account.Id);
    }

    [Fact]
    public async Task Anyone_signed_in_sees_the_basic_card_but_not_the_internal_data()
    {
        var card = await Card(User(coachId: Guid.NewGuid()));

        Assert.Equal(("Mateo García Martínez", "CD Vallobín", "1ª AsturFútbol"), (card.Entry.FullName, card.Team.Name, card.Team.CompetitionName));
        Assert.True(card.HasAccount);
        Assert.False(card.Extended);
        Assert.Null(card.InternalProfileLink);
        Assert.False(card.IsMe);
    }

    [Fact]
    public async Task The_staff_of_the_squad_sees_everything_and_its_internal_profile()
    {
        var card = await Card(User(coachId: _coachId));

        Assert.True(card.Extended);
        Assert.StartsWith($"/plantillas/{_squad.Id}/jugadores/", card.InternalProfileLink);
    }

    [Fact]
    public async Task A_public_profile_is_complete_for_everyone_and_the_player_sees_it_is_his()
    {
        _account.SetPublicProfile(true);

        var stranger = await Card(User(coachId: Guid.NewGuid()));
        Assert.True((stranger.Extended, stranger.ProfilePublic) == (true, true));
        Assert.Null(stranger.InternalProfileLink);

        var me = await Card(User(playerId: _account.Id));
        Assert.True(me.IsMe);
    }

    [Fact]
    public async Task A_card_of_another_team_is_not_found()
    {
        var handler = Handler(User(coachId: Guid.NewGuid()));
        await Assert.ThrowsAsync<TercerTiempo.Application.Common.NotFoundException>(() => handler.Handle(new GetPlayerCardQuery(Guid.NewGuid(), _entry.Id), default));
    }

    private Task<PlayerCardDto> Card(ICurrentUser user) => Handler(user).Handle(new GetPlayerCardQuery(_team.Id, _entry.Id), default);

    private GetPlayerCardHandler Handler(ICurrentUser user) => new(
        user,
        new RosterStores(
            Stub<ITeamStore>((method, args) => method switch
            {
                nameof(ITeamStore.GetRosterEntryAsync) => Task.FromResult<RosterEntry?>(_entry),
                nameof(ITeamStore.GetByIdAsync) => Task.FromResult<Team?>(_team),
                nameof(ITeamStore.GetRosterAsync) => Task.FromResult<IReadOnlyList<RosterEntry>>([_entry]),
                nameof(ITeamStore.GetByIdsAsync) => Task.FromResult<IReadOnlyList<Team>>([_team]),
                _ => throw new NotSupportedException(method),
            }),
            Stub<IMatchStore>((_, _) => Task.FromResult<IReadOnlyList<Match>>([])),
            Stub<IOfficialSheetStore>((_, _) => Task.FromResult<IReadOnlyList<OfficialMatchSheet>>([]))),
        Stub<ISquadStore>((method, _) => method == nameof(ISquadStore.GetByTeamAndSeasonAsync) ? Task.FromResult<Squad?>(_squad) : throw new NotSupportedException(method)),
        Stub<IPerformanceStore>((_, _) => Task.FromResult<IReadOnlyList<PlayerMatchPerformance>>([])),
        Stub<IPlayerStore>((_, _) => Task.FromResult<Player?>(_account)),
        Stub<ICompetitionStore>((_, _) => Task.FromResult<Competition?>(_competition)),
        Stub<IClubStore>((_, _) => Task.FromResult<Club?>(null)),
        TimeProvider.System);

    private static ICurrentUser User(Guid? coachId = null, Guid? playerId = null) => Stub<ICurrentUser>((method, _) => method switch
    {
        "get_IsAuthenticated" => true,
        "get_UserId" => Guid.NewGuid(),
        "get_CoachId" => coachId,
        "get_PlayerId" => playerId,
        "get_IsAdmin" => false,
        _ => null,
    });

    private static T Stub<T>(Func<string, object?[]?, object?> call) where T : class
    {
        var result = DispatchProxy.Create<T, PlayerInvitationTests.StoreProxy>();
        ((PlayerInvitationTests.StoreProxy)(object)result).Call = call;
        return result;
    }
}
