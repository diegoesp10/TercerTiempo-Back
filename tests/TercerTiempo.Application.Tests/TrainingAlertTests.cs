using System.Reflection;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Trainings;
using TercerTiempo.Domain.Alerts;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Trainings;

namespace TercerTiempo.Application.Tests;

// Entrenamientos y convocatorias: los avisos de la campanita que van al jugador (va o no va convocado) y al cuerpo
// técnico y al club (un jugador falta al entrenamiento; los partidos no llevan avisos del jugador), y los jugadores
// que vienen a prueba.
public class TrainingAlertTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Publishing_the_call_up_tells_each_player_with_an_account_whether_he_goes()
    {
        var f = new Fixture();

        var dto = await f.SaveCallUp().Handle(new(f.Squad.Id, f.Upcoming.Id, [f.Pelayo.Id], f.Upcoming.KickoffAt.AddHours(-2), "Puerta del campo", null, Publish: true), default);

        Assert.Equal(CallUpStatus.Published, dto.Status);
        // Pendientes: los convocados que aún no han contestado; el que no va solo se da por enterado.
        Assert.Equal((1, 1, 0), (dto.Counts.Called, dto.Counts.Pending, dto.Counts.Acknowledged));
        var called = Assert.Single(f.Alerts, a => a.PlayerId == f.PelayoAccount.Id);
        var notCalled = Assert.Single(f.Alerts, a => a.PlayerId == f.NelAccount.Id);
        Assert.Equal((AlertKind.CallUp, "/mis-convocatorias"), (called.Kind, called.Link));
        Assert.StartsWith("Vas convocado: CD Vallobín - Real Titánico", called.Title);
        Assert.Contains("Cita: ", called.Message);
        Assert.Equal(AlertKind.NotCalledUp, notCalled.Kind);
        Assert.StartsWith("No vas convocado", notCalled.Title);
        // Xuan no tiene cuenta: no hay a quién avisar, pero su línea queda marcada como notificada.
        Assert.Equal(2, f.Alerts.Count);
        Assert.All(f.CallUp.Entries, e => Assert.NotNull(e.NotifiedAt));
        Assert.Equal(1, f.Saves);
    }

    [Fact]
    public async Task A_draft_tells_nobody_and_republishing_only_tells_who_changes()
    {
        var f = new Fixture();
        await f.SaveCallUp().Handle(new(f.Squad.Id, f.Upcoming.Id, [f.Pelayo.Id], null, null, null, Publish: false), default);
        Assert.Empty(f.Alerts);
        Assert.Equal(CallUpStatus.Draft, f.CallUp.Status);

        await f.SaveCallUp().Handle(new(f.Squad.Id, f.Upcoming.Id, [f.Pelayo.Id], null, null, null, Publish: true), default);
        Assert.Equal(2, f.Alerts.Count);

        // Nel entra en la convocatoria: su aviso anterior («no vas») se resuelve y recibe el nuevo; Pelayo no vuelve a oír nada.
        f.Clock.Advance(TimeSpan.FromMinutes(10));
        await f.SaveCallUp().Handle(new(f.Squad.Id, f.Upcoming.Id, [f.Pelayo.Id, f.Nel.Id], null, null, null, Publish: false), default);

        Assert.Equal(3, f.Alerts.Count);
        var nelAlerts = f.Alerts.Where(a => a.PlayerId == f.NelAccount.Id).OrderBy(a => a.CreatedAt).ToList();
        Assert.Equal([true, false], nelAlerts.Select(a => a.IsResolved));
        Assert.Equal(AlertKind.CallUp, nelAlerts[1].Kind);
        Assert.False(Assert.Single(f.Alerts, a => a.PlayerId == f.PelayoAccount.Id).IsResolved);
    }

    [Fact]
    public async Task Signing_the_call_up_clears_the_player_alert_and_tells_the_staff_nothing()
    {
        var f = new Fixture();
        await f.SaveCallUp().Handle(new(f.Squad.Id, f.Upcoming.Id, [f.Pelayo.Id], null, null, null, Publish: true), default);

        var mine = await f.Respond(f.PelayoUser).Handle(new(f.CallUp.Id, CallUpResponse.Confirmed), default);

        Assert.Equal(CallUpResponse.Confirmed, mine.Mine.Response);
        Assert.True(Assert.Single(f.Alerts, a => a.PlayerId == f.PelayoAccount.Id).IsResolved);
        // Los partidos no llevan avisos del jugador: la firma se ve en la convocatoria, la campanita del staff no suena.
        Assert.DoesNotContain(f.Alerts, a => a.CoachId == f.Coach.Id || a.ClubId == f.Club.Id);
        Assert.Equal(2, f.Alerts.Count);
    }

    [Fact]
    public async Task A_player_outside_the_squad_cannot_answer_and_the_not_called_one_only_acknowledges()
    {
        var f = new Fixture();
        await f.SaveCallUp().Handle(new(f.Squad.Id, f.Upcoming.Id, [f.Pelayo.Id], null, null, null, Publish: true), default);

        var stranger = await Assert.ThrowsAsync<ForbiddenException>(() => f.Respond(f.StrangerUser).Handle(new(f.CallUp.Id, CallUpResponse.Confirmed), default));
        var notCalled = await Assert.ThrowsAsync<DomainException>(() => f.Respond(f.NelUser).Handle(new(f.CallUp.Id, CallUpResponse.Confirmed), default));
        var seen = await f.Respond(f.NelUser).Handle(new(f.CallUp.Id, CallUpResponse.Acknowledged), default);

        Assert.Equal(ErrorCode.SquadPrivate, stranger.Code);
        Assert.Equal(ErrorCode.CallUpNotCalled, notCalled.Code);
        Assert.Equal(CallUpResponse.Acknowledged, seen.Mine.Response);
        Assert.True(Assert.Single(f.Alerts, a => a.PlayerId == f.NelAccount.Id).IsResolved);
    }

    [Fact]
    public async Task A_weekly_plan_creates_one_session_per_date_sharing_the_series_and_the_exercises()
    {
        var f = new Fixture();
        // Lunes 19 de octubre de 2026 a las 18:00 UTC; lunes y miércoles hasta el viernes 30.
        var first = new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);
        var items = new List<TrainingItemInput> { new(null, "Calentamiento", ExerciseType.Warmup, 15, null), new(null, "Rondo", ExerciseType.Technical, 20, null) };

        var created = await f.Plan().Handle(new(f.Squad.Id, TrainingKind.Season, null, first, 90, "La Florida", null, items, [DayOfWeek.Monday, DayOfWeek.Wednesday], new DateOnly(2026, 10, 30)), default);

        Assert.Equal(4, created.Count);
        Assert.Equal(4, f.Trainings.Count);
        var seriesId = Assert.Single(f.Trainings.Select(s => s.SeriesId).Distinct());
        Assert.NotNull(seriesId);
        Assert.All(f.Trainings, s => Assert.Equal(35, s.PlannedMinutes));
        Assert.All(created, s => Assert.Equal((3, 2, "Entrenamiento de la semana"), (s.Counts.Expected, s.ItemsCount, s.Title)));
        Assert.Equal(1, f.Saves);

        var single = await f.Plan().Handle(new(f.Squad.Id, TrainingKind.Recovery, "Suave", first.AddDays(7), 60, null, null, [], [], null), default);
        Assert.Null(Assert.Single(single).SeriesId);
    }

    [Fact]
    public async Task A_player_warning_of_an_absence_alerts_the_staff_and_coming_after_all_clears_it()
    {
        var f = new Fixture();
        var planned = await f.Plan().Handle(new(f.Squad.Id, TrainingKind.Season, null, Now.AddDays(1), 90, null, null, [], [], null), default);
        var session = Assert.Single(planned);

        var detail = await f.ReportAbsence(f.PelayoUser).Handle(new(session.Id, true, "Examen"), default);

        Assert.Equal((AttendanceStatus.WillMiss, "Examen"), (detail.Mine!.Status, detail.Mine.Reason));
        Assert.Equal((2, 1), (detail.Counts.Expected, detail.Counts.WillMiss));
        Assert.False(detail.CanEdit);
        var coachAlert = Assert.Single(f.Alerts, a => a.CoachId == f.Coach.Id);
        Assert.Single(f.Alerts, a => a.ClubId == f.Club.Id);
        Assert.Equal((AlertKind.TrainingAbsence, "Pelayo Fernández falta al entrenamiento"), (coachAlert.Kind, coachAlert.Title));
        Assert.Contains("de CD Vallobín. Motivo: Examen.", coachAlert.Message);
        Assert.Equal($"/plantillas/{f.Squad.Id}/entrenamientos?sesion={session.Id}", coachAlert.Link);

        f.Clock.Advance(TimeSpan.FromMinutes(1));
        var back = await f.ReportAbsence(f.PelayoUser).Handle(new(session.Id, false, null), default);

        Assert.Equal(AttendanceStatus.Expected, back.Mine!.Status);
        Assert.All(f.Alerts, a => Assert.True(a.IsResolved));
    }

    [Fact]
    public async Task A_stranger_cannot_report_an_absence_nor_save_a_call_up()
    {
        var f = new Fixture();
        var planned = await f.Plan().Handle(new(f.Squad.Id, TrainingKind.Season, null, Now.AddDays(1), 90, null, null, [], [], null), default);

        var absence = await Assert.ThrowsAsync<ForbiddenException>(() => f.ReportAbsence(f.StrangerUser).Handle(new(planned[0].Id, true, null), default));
        var callUp = await Assert.ThrowsAsync<ForbiddenException>(() => f.SaveCallUp(f.NelUser).Handle(new(f.Squad.Id, f.Upcoming.Id, [f.Pelayo.Id], null, null, null, true), default));

        Assert.Equal(ErrorCode.SquadPrivate, absence.Code);
        Assert.Equal(ErrorCode.SquadManagersOnly, callUp.Code);
        Assert.Empty(f.Alerts);
    }

    [Fact]
    public async Task A_trial_player_joins_a_session_by_name_and_is_reused_by_that_name()
    {
        var f = new Fixture();
        var planned = await f.Plan().Handle(new(f.Squad.Id, TrainingKind.Season, null, Now.AddDays(1), 90, null, null, [], [DayOfWeek.Saturday, DayOfWeek.Sunday], DateOnly.FromDateTime(Now.AddDays(8).DateTime)), default);
        var first = planned[0].Id;
        var second = planned[1].Id;

        var detail = await f.AddTrial().Handle(new(f.Squad.Id, first, null, "  Iyán Cuervo ", "Viene del Llanera"), default);
        await f.AddTrial().Handle(new(f.Squad.Id, second, null, "iyan cuervo", null), default);

        var trial = Assert.Single(f.Trials);
        Assert.Equal(("Iyán Cuervo", "Viene del Llanera", false), (trial.FullName, trial.Notes, trial.IsPromoted));
        var line = Assert.Single(detail.Attendance, a => a.Trial);
        Assert.Equal((trial.Id, "Iyán Cuervo", AttendanceStatus.Expected), (line.TrialPlayerId, line.PlayerName, line.Status));
        // Tres de la plantilla más el de prueba.
        Assert.Equal(4, detail.Counts.Expected);
        Assert.All(f.Trainings.Take(2), s => Assert.NotNull(s.TrialAttendanceOf(trial.Id)));

        // El jugador de la plantilla lo ve en la lista pero no puede pasarle lista.
        var seen = await f.ReportAbsence(f.PelayoUser).Handle(new(first, false, null), default);
        Assert.Contains(seen.Attendance, a => a.Trial && a.TrialPlayerId == trial.Id);
    }

    [Fact]
    public async Task Promoting_a_trial_player_adds_him_to_the_squad_and_keeps_his_attendance()
    {
        var f = new Fixture();
        var planned = await f.Plan().Handle(new(f.Squad.Id, TrainingKind.Season, null, Now.AddDays(1), 90, null, null, [], [], null), default);
        var session = planned[0].Id;
        await f.AddTrial().Handle(new(f.Squad.Id, session, null, "Iyán Cuervo", null), default);
        var trial = f.Trials.Single();
        await f.SetAttendance().Handle(new(f.Squad.Id, session, [new(null, trial.Id, AttendanceStatus.Attended, null)]), default);

        var card = await f.Promote().Handle(new(f.Squad.Id, trial.Id, Position.Midfielder, 14), default);

        Assert.Equal(("Iyán Cuervo", Position.Midfielder, 14), (card.FullName, card.Position, card.ShirtNumber));
        Assert.Contains(f.Squad.ActivePlayers, p => p.Id == card.Id);
        Assert.Equal((card.Id, Now), (trial.PromotedSquadPlayerId, trial.PromotedAt));
        var line = f.Trainings.Single().AttendanceOf(card.Id);
        Assert.NotNull(line);
        Assert.Equal((AttendanceStatus.Attended, (Guid?)null), (line.Status, line.TrialPlayerId));
        Assert.Null(f.Trainings.Single().TrialAttendanceOf(trial.Id));

        var again = await Assert.ThrowsAsync<DomainException>(() => f.Promote().Handle(new(f.Squad.Id, trial.Id, null, null), default));
        Assert.Equal(ErrorCode.TrialPlayerPromoted, again.Code);
    }

    private sealed class Fixture
    {
        public FakeClock Clock { get; } = new(Now);
        public Club Club { get; } = Club.Register(Guid.NewGuid(), "CD Vallobín", Now);
        public Team Team { get; } = Team.Create("CD Vallobín", "VAL", null);
        public Team Rival { get; } = Team.Create("Real Titánico", "TIT", null);
        public Coach Coach { get; } = Coach.Register(Guid.NewGuid(), "Abel Pablo", Now);
        public Player PelayoAccount { get; } = Player.Register(Guid.NewGuid(), "Pelayo Fernández", Position.Forward, null, Now);
        public Player NelAccount { get; } = Player.Register(Guid.NewGuid(), "Nel Suárez", Position.Goalkeeper, null, Now);
        public Player Stranger { get; } = Player.Register(Guid.NewGuid(), "Un desconocido", Position.Midfielder, null, Now);
        public Squad Squad { get; }
        public SquadPlayer Pelayo { get; }
        public SquadPlayer Nel { get; }
        public SquadPlayer Xuan { get; }
        public Match Upcoming { get; }
        public List<Alert> Alerts { get; } = [];
        public List<MatchCallUp> CallUps { get; } = [];
        public List<TrainingSession> Trainings { get; } = [];
        public List<TrialPlayer> Trials { get; } = [];
        public MatchCallUp CallUp => CallUps.Single();
        public int Saves { get; private set; }

        public ICurrentUser CoachUser => new Account(CoachId: Coach.Id, UserId: Coach.UserId, Type: AccountType.Coach);
        public ICurrentUser PelayoUser => new Account(PlayerId: PelayoAccount.Id, UserId: PelayoAccount.UserId, Type: AccountType.Player);
        public ICurrentUser NelUser => new Account(PlayerId: NelAccount.Id, UserId: NelAccount.UserId, Type: AccountType.Player);
        public ICurrentUser StrangerUser => new Account(PlayerId: Stranger.Id, UserId: Stranger.UserId, Type: AccountType.Player);

        public Fixture()
        {
            var seasonId = Guid.NewGuid();
            Club.AddTeam(Team.Id, Now);
            Squad = Squad.Create(Team.Id, seasonId, Coach.Id, Now);
            Pelayo = Squad.AddPlayer("Pelayo Fernández", Position.Forward, 9, null, Now);
            Nel = Squad.AddPlayer("Nel Suárez", Position.Goalkeeper, 1, null, Now);
            Xuan = Squad.AddPlayer("Xuan Menéndez", Position.Defender, 4, null, Now);
            Squad.LinkPlayer(Pelayo.Id, PelayoAccount.Id);
            Squad.LinkPlayer(Nel.Id, NelAccount.Id);
            Upcoming = Match.Schedule(seasonId, 2, Team.Id, Rival.Id, Now.AddDays(3), "La Florida", null);
        }

        public SaveMatchCallUpHandler SaveCallUp(ICurrentUser? user = null) =>
            new(user ?? CoachUser, Squads, Coaches, Clubs, Matches, Teams, CallUpStore, AlertStore, Unit, Clock);

        public RespondCallUpHandler Respond(ICurrentUser user) =>
            new(user, Players, Squads, Matches, Teams, CallUpStore, AlertStore, Unit, Clock);

        public PlanTrainingHandler Plan() => new(CoachUser, Squads, Coaches, Clubs, TrainingStore, Unit, Clock);

        public ReportTrainingAbsenceHandler ReportAbsence(ICurrentUser user) =>
            new(user, Players, Squads, Clubs, TrainingStore, TrialStore, Teams, AlertStore, Unit, Clock);

        public SetAttendanceHandler SetAttendance() => new(CoachUser, Squads, Clubs, TrainingStore, TrialStore, Unit, Clock);

        public AddTrialPlayerToTrainingHandler AddTrial() => new(CoachUser, Squads, Coaches, Clubs, TrainingStore, TrialStore, Unit, Clock);

        public PromoteTrialPlayerHandler Promote() => new(CoachUser, Squads, Clubs, TrialStore, TrainingStore, Unit, Clock);

        private ISquadStore Squads => Stub<ISquadStore>((method, args) => method switch
        {
            nameof(ISquadStore.GetByIdAsync) => Task.FromResult((Guid)args![0]! == Squad.Id ? Squad : null),
            nameof(ISquadStore.GetByPlayerAsync) => Task.FromResult(Squad.IsMember((Guid)args![0]!) ? Squad : null),
            _ => throw new NotSupportedException(method),
        });
        private IClubStore Clubs => Stub<IClubStore>((method, args) => method switch
        {
            nameof(IClubStore.GetByTeamAsync) => Task.FromResult((Guid)args![0]! == Team.Id ? Club : null),
            nameof(IClubStore.GetByUserIdAsync) => Task.FromResult((Guid)args![0]! == Club.UserId ? Club : null),
            nameof(IClubStore.GetByIdAsync) => Task.FromResult((Guid)args![0]! == Club.Id ? Club : null),
            _ => throw new NotSupportedException(method),
        });
        private ICoachStore Coaches => Stub<ICoachStore>((method, args) => method switch
        {
            nameof(ICoachStore.GetByUserIdAsync) => Task.FromResult((Guid)args![0]! == Coach.UserId ? Coach : null),
            _ => throw new NotSupportedException(method),
        });
        private IPlayerStore Players => Stub<IPlayerStore>((method, args) => method switch
        {
            nameof(IPlayerStore.GetByUserIdAsync) => Task.FromResult(new[] { PelayoAccount, NelAccount, Stranger }.FirstOrDefault(p => p.UserId == (Guid)args![0]!)),
            _ => throw new NotSupportedException(method),
        });
        private IMatchStore Matches => Stub<IMatchStore>((method, args) => method switch
        {
            nameof(IMatchStore.GetByIdAsync) => Task.FromResult((Guid)args![0]! == Upcoming.Id ? Upcoming : null),
            nameof(IMatchStore.GetByIdsAsync) => Task.FromResult<IReadOnlyList<Match>>([Upcoming]),
            _ => throw new NotSupportedException(method),
        });
        private ITeamStore Teams => Stub<ITeamStore>((method, args) => method switch
        {
            nameof(ITeamStore.GetByIdsAsync) => Task.FromResult<IReadOnlyList<Team>>([Team, Rival]),
            nameof(ITeamStore.GetByIdAsync) => Task.FromResult(new[] { Team, Rival }.FirstOrDefault(t => t.Id == (Guid)args![0]!)),
            _ => throw new NotSupportedException(method),
        });
        private ICallUpStore CallUpStore => Stub<ICallUpStore>((method, args) => method switch
        {
            nameof(ICallUpStore.GetByIdAsync) => Task.FromResult(CallUps.FirstOrDefault(c => c.Id == (Guid)args![0]!)),
            nameof(ICallUpStore.GetByMatchAsync) => Task.FromResult(CallUps.FirstOrDefault(c => c.SquadId == (Guid)args![0]! && c.MatchId == (Guid)args[1]!)),
            nameof(ICallUpStore.GetPublishedBySquadAsync) => Task.FromResult<IReadOnlyList<MatchCallUp>>(CallUps.Where(c => c.IsPublished).ToList()),
            nameof(ICallUpStore.GetBySquadAsync) => Task.FromResult<IReadOnlyList<MatchCallUp>>(CallUps.ToList()),
            nameof(ICallUpStore.Add) => Run(() => CallUps.Add((MatchCallUp)args![0]!)),
            _ => throw new NotSupportedException(method),
        });
        private ITrainingStore TrainingStore => Stub<ITrainingStore>((method, args) => method switch
        {
            nameof(ITrainingStore.GetByIdAsync) => Task.FromResult(Trainings.FirstOrDefault(s => s.Id == (Guid)args![0]!)),
            nameof(ITrainingStore.GetByTrialPlayerAsync) => Task.FromResult<IReadOnlyList<TrainingSession>>(Trainings.Where(s => s.TrialAttendanceOf((Guid)args![0]!) is not null).ToList()),
            nameof(ITrainingStore.Add) => Run(() => Trainings.Add((TrainingSession)args![0]!)),
            _ => throw new NotSupportedException(method),
        });
        private ITrialPlayerStore TrialStore => Stub<ITrialPlayerStore>((method, args) => method switch
        {
            nameof(ITrialPlayerStore.GetByIdAsync) => Task.FromResult(Trials.FirstOrDefault(t => t.Id == (Guid)args![0]!)),
            nameof(ITrialPlayerStore.GetBySquadAsync) => Task.FromResult<IReadOnlyList<TrialPlayer>>(Trials.Where(t => t.SquadId == (Guid)args![0]!).ToList()),
            nameof(ITrialPlayerStore.Add) => Run(() => Trials.Add((TrialPlayer)args![0]!)),
            _ => throw new NotSupportedException(method),
        });
        private IAlertStore AlertStore => Stub<IAlertStore>((method, args) => method switch
        {
            nameof(IAlertStore.GetByClubAsync) => Task.FromResult<IReadOnlyList<Alert>>(Alerts.Where(a => a.ClubId == (Guid)args![0]!).ToList()),
            nameof(IAlertStore.GetForAsync) => Task.FromResult<IReadOnlyList<Alert>>(Alerts.Where(a => a.IsFor((Guid?)args![0], (Guid?)args[1], (Guid?)args[2])).ToList()),
            nameof(IAlertStore.Add) => Run(() => Alerts.Add((Alert)args![0]!)),
            _ => throw new NotSupportedException(method),
        });
        private IUnitOfWork Unit => Stub<IUnitOfWork>((_, _) => Task.FromResult(++Saves));

        private static object? Run(Action action) { action(); return null; }

        private static T Stub<T>(Func<string, object?[]?, object?> call) where T : class
        {
            var stub = DispatchProxy.Create<T, ClubAsStaffTests.StoreProxy>();
            ((ClubAsStaffTests.StoreProxy)(object)stub).Call = call;
            return stub;
        }
    }

    private sealed record Account(Guid? PlayerId = null, Guid? CoachId = null, Guid? ClubId = null, Guid? UserId = null, AccountType? Type = null) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public AccountType? AccountType => Type;
    }

    public sealed class FakeClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
