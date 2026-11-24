using System.Reflection;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Medical;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Squads.Injuries;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Tests;

// Las lesiones abiertas al cambiar de temporada pasan a la plantilla nueva de la misma persona: no sale disponible y los
// días se siguen contando desde el parte. La de la temporada anterior queda allí como historial.
public class InjuryCarryOverTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = SpainTime.Today(Now);
    private static readonly DateOnly Report = new(2026, 5, 10);

    [Fact]
    public async Task An_open_injury_passes_to_the_new_season_and_keeps_counting_from_the_report()
    {
        var f = new Fixture();
        var old = f.Injure(f.LastSquad, f.PelayoThen, Report, new DateOnly(2026, 12, 20));

        var result = await f.CarryOver.AllAsync(default);

        Assert.Equal((1, 0), (result.Carried, result.Ended));
        var next = Assert.Single(f.Injuries, i => i.SquadId == f.Squad.Id);
        Assert.Equal((f.Pelayo.Id, old.Id, Report, old.ExpectedReturnOn, old.Type, true),
            (next.SquadPlayerId, next.ContinuesInjuryId, next.StartedOn, next.ExpectedReturnOn, next.Type, next.IsActive));
        Assert.Equal(Today.DayNumber - Report.DayNumber, next.DaysOut(Today));
        // La de la temporada pasada queda allí como historial, contada hasta el día en que pasó.
        Assert.Equal((false, Today), (old.IsActive, old.ContinuedOn));
        Assert.Equal(Today.DayNumber - Report.DayNumber, old.DaysOut(Today.AddDays(30)));

        // Otra pasada no la vuelve a pasar, ni la lleva a una temporada que aún no ha empezado.
        f.OpenNextSeason();
        Assert.False((await f.CarryOver.AllAsync(default)).Changed);
    }

    [Fact]
    public async Task Opening_next_season_early_does_not_empty_this_season()
    {
        var f = new Fixture();
        var (next, _) = f.OpenNextSeason();
        var injury = f.Injure(f.Squad, f.Pelayo, Today.AddDays(-3), Today.AddDays(20));

        Assert.False((await f.CarryOver.IntoAsync(next, default)).Changed);
        Assert.True(injury.IsActive);
    }

    [Fact]
    public async Task Only_open_injuries_of_players_still_in_the_squad_pass_and_never_to_a_player_who_left()
    {
        var f = new Fixture();
        f.Injure(f.LastSquad, f.PelayoThen, new DateOnly(2026, 3, 1), null, returnedOn: new DateOnly(2026, 4, 1));
        // Causó baja la temporada pasada con la lesión abierta: el club no sabe más.
        var nelThen = f.LastSquad.AddPlayer("Nel Suárez", Position.Goalkeeper, null, null, Now.AddYears(-1));
        var leftThen = f.Injure(f.LastSquad, nelThen, Report, null);
        f.LastSquad.RemovePlayer(nelThen.Id, Now.AddMonths(-6));
        f.Squad.AddPlayer("Nel Suárez", Position.Goalkeeper, null, null, Now);
        // De baja en la temporada nueva: se pasa si vuelve.
        var xuanThen = f.LastSquad.AddPlayer("Xuan Rodríguez", Position.Defender, null, null, Now.AddYears(-1));
        var waiting = f.Injure(f.LastSquad, xuanThen, Report, null);
        var xuan = f.Squad.AddPlayer("Xuan Rodríguez", Position.Defender, null, null, Now);
        f.Squad.RemovePlayer(xuan.Id, Now);

        Assert.False((await f.CarryOver.AllAsync(default)).Changed);
        Assert.Equal((true, true), (leftThen.IsActive, waiting.IsActive));

        f.Squad.ReinstatePlayer(xuan.Id);
        Assert.Equal(1, (await f.CarryOver.IntoAsync(f.Squad, default)).Carried);
        Assert.Equal(xuan.Id, f.Injuries.Single(i => i.ContinuesInjuryId == waiting.Id).SquadPlayerId);
    }

    [Fact]
    public async Task If_the_new_season_already_tracks_it_the_old_one_passes_without_duplicating()
    {
        var f = new Fixture();
        var old = f.Injure(f.LastSquad, f.PelayoThen, Report, null);
        f.Injure(f.Squad, f.Pelayo, new DateOnly(2026, 8, 3), Today.AddDays(5));

        var result = await f.CarryOver.AllAsync(default);

        Assert.Equal((0, 1), (result.Carried, result.Ended));
        Assert.Single(f.Injuries, i => i.SquadId == f.Squad.Id);
        Assert.Equal(Today, old.ContinuedOn);
    }

    [Fact]
    public async Task With_an_account_it_follows_the_person_to_another_team_and_with_doubts_it_stays()
    {
        var f = new Fixture();
        var account = Guid.NewGuid();
        var abelThen = f.LastSquad.AddPlayer("Abel Pablo", Position.Midfielder, null, null, Now.AddYears(-1));
        f.LastSquad.LinkPlayer(abelThen.Id, account);
        var abelInjury = f.Injure(f.LastSquad, abelThen, Report, null);
        var bSquad = f.Open(f.BTeam, f.This);
        var abel = bSquad.AddPlayer("Abel P.", Position.Midfielder, null, null, Now);
        bSquad.LinkPlayer(abel.Id, account);
        // Sin cuenta, dos fichas de este año encajan con su nombre: no se sabe cuál es.
        var lucasThen = f.LastSquad.AddPlayer("Lucas Paredes", Position.Forward, null, null, Now.AddYears(-1));
        var doubtful = f.Injure(f.LastSquad, lucasThen, Report, null);
        f.Squad.AddPlayer("Lucas Paredes", Position.Forward, null, null, Now);
        f.Squad.AddPlayer("Lucas Paredes Rivera", Position.Defender, null, null, Now);

        var result = await f.CarryOver.AllAsync(default);

        Assert.Equal(1, result.Carried);
        Assert.Equal((bSquad.Id, abel.Id), f.Injuries.Where(i => i.ContinuesInjuryId == abelInjury.Id).Select(i => (i.SquadId, i.SquadPlayerId)).Single());
        Assert.True(doubtful.IsActive);
    }

    [Fact]
    public async Task The_profile_shows_a_carried_injury_once_with_the_season_of_its_report()
    {
        var f = new Fixture();
        var old = f.Injure(f.LastSquad, f.PelayoThen, Report, new DateOnly(2026, 12, 20));
        await f.CarryOver.AllAsync(default);

        var history = await f.History().Handle(new GetPlayerInjuryHistoryQuery(f.Squad.Id, f.Pelayo.Id), default);

        var entry = Assert.Single(history.Injuries);
        Assert.Equal((false, "2025/2026", "2026/2027", true, old.Id), (entry.OtherSquad, entry.FromSeasonLabel, entry.SeasonLabel, entry.Injury.Active, entry.Injury.ContinuesInjuryId));
        Assert.Equal((1, Today.DayNumber - Report.DayNumber), (history.Totals.Injuries, history.Totals.DaysOut));
    }

    [Fact]
    public async Task The_old_one_is_only_consulted_and_deleting_either_keeps_the_other()
    {
        var f = new Fixture();
        var old = f.Injure(f.LastSquad, f.PelayoThen, Report, null);
        await f.CarryOver.AllAsync(default);
        var next = f.Injuries.Single(i => i.ContinuesInjuryId == old.Id);

        var close = await Assert.ThrowsAsync<DomainException>(() => f.Close().Handle(new CloseInjuryCommand(f.LastSquad.Id, old.Id, Today), default));
        Assert.Equal(ErrorCode.InjuryContinued, close.Code);
        var reopen = await Assert.ThrowsAsync<DomainException>(() => f.Reopen().Handle(new ReopenInjuryCommand(f.LastSquad.Id, old.Id), default));
        Assert.Equal(ErrorCode.InjuryContinued, reopen.Code);

        // El alta se da donde se lleva; la de la temporada pasada no cambia.
        await f.Close().Handle(new CloseInjuryCommand(f.Squad.Id, next.Id, Today), default);
        Assert.Equal((Today, Today.DayNumber - Report.DayNumber), (next.ReturnedOn, next.DaysOut(Today)));
        Assert.Null(old.ReturnedOn);

        // Borrar la de la temporada pasada deja la continuación con su parte, ya sin enlace.
        await f.Delete().Handle(new DeleteInjuryCommand(f.LastSquad.Id, old.Id), default);
        Assert.Equal(((Guid?)null, Report), (next.ContinuesInjuryId, next.StartedOn));
    }

    [Fact]
    public async Task Deleting_the_continuation_does_not_bring_it_back()
    {
        var f = new Fixture();
        var old = f.Injure(f.LastSquad, f.PelayoThen, Report, null);
        await f.CarryOver.AllAsync(default);
        var next = f.Injuries.Single(i => i.ContinuesInjuryId == old.Id);

        await f.Delete().Handle(new DeleteInjuryCommand(f.Squad.Id, next.Id), default);

        Assert.False((await f.CarryOver.AllAsync(default)).Changed);
        Assert.Equal((false, Today), (old.IsActive, old.ContinuedOn));
    }

    private sealed record Account(Guid? PlayerId = null, Guid? CoachId = null, Guid? ClubId = null, Guid? UserId = null, AccountType? Type = null, bool Admin = false) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public AccountType? AccountType => Type;
        public bool IsAdmin => Admin;
    }

    private sealed class Fixture
    {
        public Club Club { get; } = Club.Register(Guid.NewGuid(), "CD Vallobín", Now);
        public Team Team { get; } = Team.Create("CD Vallobín", "VAL", null);
        public Team BTeam { get; } = Team.Create("CD Vallobín B", "VAB", null);
        public Competition Competition { get; } = Competition.Create("Liga", "Primera", CompetitionLevel.Regional, 1, null, "Asturias", null);
        public SeasonYear LastYear { get; } = SeasonYear.Create("2025/2026", new(2025, 8, 1), new(2026, 6, 30));
        public SeasonYear ThisYear { get; } = SeasonYear.Create("2026/2027", new(2026, 8, 1), new(2027, 6, 30));
        public SeasonYear NextYear { get; } = SeasonYear.Create("2027/2028", new(2027, 8, 1), new(2028, 6, 30));
        public Season Last { get; }
        public Season This { get; }
        public Season Next { get; }
        public Squad Squad { get; }
        public Squad LastSquad { get; }
        public SquadPlayer Pelayo { get; }
        public SquadPlayer PelayoThen { get; }
        public List<Squad> Squads { get; } = [];
        public List<PlayerInjury> Injuries { get; } = [];

        public ICurrentUser ClubUser => new Account(ClubId: Club.Id, UserId: Club.UserId, Type: AccountType.Club);

        public Fixture()
        {
            SeasonYear.MarkCurrent([LastYear, ThisYear, NextYear], ThisYear.Id);
            Last = Competition.AddSeason("2025/2026", new(2025, 8, 1), new(2026, 6, 30), null, LastYear.Id);
            This = Competition.AddSeason("2026/2027", new(2026, 8, 1), new(2027, 6, 30), null, ThisYear.Id);
            Next = Competition.AddSeason("2027/2028", new(2027, 8, 1), new(2028, 6, 30), null, NextYear.Id);
            Club.AddTeam(Team.Id, Now);
            Club.AddTeam(BTeam.Id, Now);

            Squad = Open(Team, This);
            Pelayo = Squad.AddPlayer("Pelayo Fernández", Position.Forward, null, null, Now);
            Squad.LinkPlayer(Pelayo.Id, Guid.NewGuid());
            // La temporada pasada no tenía cuenta: se le encuentra por el nombre en el mismo equipo.
            LastSquad = Open(Team, Last);
            PelayoThen = LastSquad.AddPlayer("Pelayo Fernández", Position.Forward, null, null, Now.AddYears(-1));
        }

        public Squad Open(Team team, Season season)
        {
            var squad = Squad.Open(team.Id, season.Id, Now.AddYears(season == Last ? -1 : 0));
            Squads.Add(squad);
            return squad;
        }

        public (Squad Squad, SquadPlayer Pelayo) OpenNextSeason()
        {
            var squad = Open(Team, Next);
            return (squad, squad.AddPlayer("Pelayo Fernández", Position.Forward, null, null, Now));
        }

        public PlayerInjury Injure(Squad squad, SquadPlayer player, DateOnly startedOn, DateOnly? expected, DateOnly? returnedOn = null)
        {
            var reportedAt = new DateTimeOffset(startedOn.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);
            var injury = PlayerInjury.Create(squad, player, SquadEditor.Club(Club), TestInjuryTypes.Of(InjuryTypeCodes.AnkleSprain), null, startedOn, expected, reportedAt);
            if (returnedOn is { } day) injury.Close(day, reportedAt.AddDays(day.DayNumber - startedOn.DayNumber));
            Injuries.Add(injury);
            return injury;
        }

        public InjuryCarryOver CarryOver => new(SquadStore, Clubs, InjuryStore, Competitions, Years, Clock);
        public GetPlayerInjuryHistoryHandler History() => new(ClubUser, SquadStore, Clubs, InjuryStore, DocumentStore, Teams, Competitions, Clock);
        public CloseInjuryHandler Close() => new(ClubUser, SquadStore, Clubs, InjuryStore, DocumentStore, Unit, Clock);
        public ReopenInjuryHandler Reopen() => new(ClubUser, SquadStore, Clubs, InjuryStore, DocumentStore, Unit, Clock);
        public DeleteInjuryHandler Delete() => new(ClubUser, SquadStore, Clubs, InjuryStore, DocumentStore, Storage, Unit, Clock);

        private static TimeProvider Clock => new FixedClock();

        private ISquadStore SquadStore => Stub<ISquadStore>((method, args) => method switch
        {
            nameof(ISquadStore.GetByIdAsync) => Task.FromResult(Squads.FirstOrDefault(s => s.Id == (Guid)args![0]!)),
            nameof(ISquadStore.GetByIdsAsync) => Task.FromResult<IReadOnlyList<Squad>>(
                Squads.Where(s => ((IEnumerable<Guid>)args![0]!).Contains(s.Id)).ToList()),
            nameof(ISquadStore.GetByTeamIdsAsync) when args!.Length == 2 => Task.FromResult<IReadOnlyList<Squad>>(
                Squads.Where(s => ((IEnumerable<Guid>)args[0]!).Contains(s.TeamId)).ToList()),
            _ => throw new NotSupportedException(method),
        });

        private IClubStore Clubs => Stub<IClubStore>((method, args) => method switch
        {
            nameof(IClubStore.GetByIdAsync) => Task.FromResult((Guid)args![0]! == Club.Id ? Club : null),
            nameof(IClubStore.GetByUserIdAsync) => Task.FromResult((Guid)args![0]! == Club.UserId ? Club : null),
            nameof(IClubStore.GetByTeamAsync) => Task.FromResult(Club.Manages((Guid)args![0]!) ? Club : null),
            _ => throw new NotSupportedException(method),
        });

        private ITeamStore Teams => Stub<ITeamStore>((method, _) => method switch
        {
            nameof(ITeamStore.GetByIdsAsync) => Task.FromResult<IReadOnlyList<Team>>([Team, BTeam]),
            _ => throw new NotSupportedException(method),
        });

        private ICompetitionStore Competitions => Stub<ICompetitionStore>((method, _) => method switch
        {
            nameof(ICompetitionStore.GetBySeasonIdsAsync) => Task.FromResult<IReadOnlyList<Competition>>([Competition]),
            _ => throw new NotSupportedException(method),
        });

        private ISeasonYearStore Years => Stub<ISeasonYearStore>((method, _) => method switch
        {
            nameof(ISeasonYearStore.GetAllAsync) => Task.FromResult<IReadOnlyList<SeasonYear>>([NextYear, ThisYear, LastYear]),
            _ => throw new NotSupportedException(method),
        });

        private IInjuryStore InjuryStore => Stub<IInjuryStore>((method, args) => method switch
        {
            nameof(IInjuryStore.GetByIdAsync) => Task.FromResult(Injuries.FirstOrDefault(i => i.Id == (Guid)args![0]!)),
            nameof(IInjuryStore.GetBySquadAsync) => Task.FromResult<IReadOnlyList<PlayerInjury>>(Injuries.Where(i => i.SquadId == (Guid)args![0]!).ToList()),
            nameof(IInjuryStore.GetBySquadPlayersAsync) => Task.FromResult<IReadOnlyList<PlayerInjury>>(
                Injuries.Where(i => ((IReadOnlyCollection<Guid>)args![0]!).Contains(i.SquadPlayerId)).ToList()),
            nameof(IInjuryStore.GetOpenAsync) => Task.FromResult<IReadOnlyList<PlayerInjury>>(
                Injuries.Where(i => i.ReturnedOn is null && i.ContinuedAt is null && (args![0] is not IReadOnlyCollection<Guid> ids || ids.Contains(i.SquadId))).ToList()),
            nameof(IInjuryStore.GetContinuationAsync) => Task.FromResult(Injuries.FirstOrDefault(i => i.ContinuesInjuryId == (Guid)args![0]!)),
            nameof(IInjuryStore.Add) => Add((PlayerInjury)args![0]!),
            nameof(IInjuryStore.Remove) => Injuries.Remove((PlayerInjury)args![0]!),
            _ => throw new NotSupportedException(method),
        });

        private object? Add(PlayerInjury injury)
        {
            Injuries.Add(injury);
            return null;
        }

        private static IInjuryDocumentStore DocumentStore => Stub<IInjuryDocumentStore>((method, _) => method switch
        {
            nameof(IInjuryDocumentStore.GetByInjuryIdsAsync) => Task.FromResult<IReadOnlyList<InjuryDocument>>([]),
            _ => throw new NotSupportedException(method),
        });

        private static IInjuryDocumentStorage Storage => Stub<IInjuryDocumentStorage>((_, _) => Task.CompletedTask);

        private static IUnitOfWork Unit => Stub<IUnitOfWork>((_, _) => Task.FromResult(1));
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static T Stub<T>(Func<string, object?[]?, object?> call) where T : class
    {
        var stub = DispatchProxy.Create<T, PlayerInvitationTests.StoreProxy>();
        ((PlayerInvitationTests.StoreProxy)(object)stub).Call = call;
        return stub;
    }
}
