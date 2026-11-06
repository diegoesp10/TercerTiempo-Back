using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Squads.MatchSheet;
using TercerTiempo.Application.Videos.Analysis;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Application.Tests;

// La propuesta del vídeo en la API: quién la ve, quién la relanza y cómo se vuelca al acta interna.
public class VideoAnalysisApplyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task The_staff_sees_the_state_of_each_video_and_the_proposal_when_it_is_ready()
    {
        var f = new Fixture();
        var waiting = await f.Get(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id), default);
        Assert.True(waiting is { Enabled: true, CanManage: true });
        Assert.Equal(VideoAnalysisState.NotRequested, Assert.Single(waiting.Videos).State);

        f.Analyzed();
        var ready = Assert.Single((await f.Get(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id), default)).Videos);

        Assert.Equal(VideoAnalysisState.Ready, ready.State);
        Assert.Equal((19.3m, 67.1m, true, false), (ready.Quality!.BallFramesPct, ready.Quality.CalibratedFramesPct, ready.Quality.LowBall, ready.Quality.LowCalibration));
        Assert.Equal([(0, 38, 2), (1, 62, 1)], ready.Teams.Select(t => (t.Index, t.PossessionPct ?? -1, t.Players)));
        Assert.Equal([696, 781, 12], ready.Players.Select(p => p.Tracklet));
        // Solo tienen recorte los jugadores cuyo recorte guardó el analizador.
        Assert.Equal($"/api/squads/{f.Squad.Id}/matches/{f.Played.Id}/video-analysis/crops/696?videoId={f.Video.Id}&v={f.Analysis.FinishedAt!.Value.ToUnixTimeSeconds()}", ready.Players[0].CropUrl);
        Assert.Null(ready.Players[2].CropUrl);
        Assert.All(ready.Players, p => Assert.Null(p.SquadPlayerId));
    }

    [Fact]
    public async Task A_video_still_being_compressed_waits_and_no_video_means_an_empty_list()
    {
        var f = new Fixture();
        f.Videos.Items.Clear();
        Assert.Empty((await f.Get(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id), default)).Videos);

        var uploading = MatchVideo.Upload(f.Squad.Id, f.Team.Id, f.Played.Id, f.Squad.SeasonId, null, VideoPart.Full, null, "partido.mp4", "video/mp4",
            50_000_000, "original.mp4", Now, f.Club.Id);
        f.Videos.Items.Add(uploading);

        Assert.Equal(VideoAnalysisState.WaitingForVideo, Assert.Single((await f.Get(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id), default)).Videos).State);
        var error = await Assert.ThrowsAsync<ConflictException>(() => f.Request(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id, null), default));
        Assert.Equal(ErrorCode.VideoAnalysisVideoNotReady, error.Code);
    }

    [Fact]
    public async Task The_admin_only_looks_and_strangers_see_nothing()
    {
        var f = new Fixture();
        f.Analyzed();
        var admin = new Admin(Guid.NewGuid());
        var other = Club.Register(Guid.NewGuid(), "Real Titánico", Now);
        other.AddTeam(f.Rival.Id, Now);
        f.OtherClubs.Add(other);
        var stranger = new User(ClubId: other.Id, UserId: other.UserId);

        var seen = await f.Get(admin).Handle(new(f.Squad.Id, f.Played.Id), default);
        Assert.False(seen.CanManage);
        Assert.Equal(VideoAnalysisState.Ready, Assert.Single(seen.Videos).State);
        Assert.Equal([0xFF, 0xD8, 0xFF, 0xD9], (await f.Crop(admin).Handle(new(f.Squad.Id, f.Played.Id, 696, null), default)).Content);

        Assert.Equal(ErrorCode.SquadManagersOnly, (await Assert.ThrowsAsync<ForbiddenException>(() => f.Request(admin).Handle(new(f.Squad.Id, f.Played.Id, null), default))).Code);
        Assert.Equal(ErrorCode.SquadManagersOnly, (await Assert.ThrowsAsync<ForbiddenException>(() => f.Apply(admin).Handle(f.Everything(), default))).Code);
        Assert.Equal(ErrorCode.SquadManagersOnly, (await Assert.ThrowsAsync<ForbiddenException>(() => f.Get(stranger).Handle(new(f.Squad.Id, f.Played.Id), default))).Code);
        Assert.Equal(ErrorCode.SquadManagersOnly, (await Assert.ThrowsAsync<ForbiddenException>(() => f.Crop(stranger).Handle(new(f.Squad.Id, f.Played.Id, 696, null), default))).Code);
        Assert.Equal(0, f.Saves);
    }

    [Fact]
    public async Task Asking_again_forgets_the_old_proposal_and_is_refused_while_queued_or_disabled()
    {
        var f = new Fixture();
        f.Analyzed();

        var requested = await f.Request(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id, f.Video.Id), default);

        Assert.Equal(VideoAnalysisState.Pending, Assert.Single(requested.Videos).State);
        Assert.Empty(requested.Videos[0].Players);
        Assert.Null(f.Analysis.ProposalJson);
        Assert.Equal(1, f.Signal.Wakes);

        Assert.Equal(ErrorCode.VideoAnalysisAlreadyQueued,
            (await Assert.ThrowsAsync<ConflictException>(() => f.Request(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id, f.Video.Id), default))).Code);
        Assert.Equal(ErrorCode.VideoNotFound,
            (await Assert.ThrowsAsync<NotFoundException>(() => f.Request(f.ClubUser).Handle(new(f.Squad.Id, f.Played.Id, Guid.NewGuid()), default))).Code);

        var disabled = new Fixture(enabled: false);
        Assert.False((await disabled.Get(disabled.ClubUser).Handle(new(disabled.Squad.Id, disabled.Played.Id), default)).Enabled);
        Assert.Equal(ErrorCode.VideoAnalysisDisabled,
            (await Assert.ThrowsAsync<ConflictException>(() => disabled.Request(disabled.ClubUser).Handle(new(disabled.Squad.Id, disabled.Played.Id, null), default))).Code);
    }

    [Fact]
    public async Task What_the_staff_accepts_goes_to_the_sheet_in_its_name_and_the_assignment_is_remembered()
    {
        var f = new Fixture();
        f.Analyzed();

        var result = await f.Apply(f.ClubUser).Handle(f.Everything(), default);

        // Datos de equipo, vistos desde el equipo que el técnico dijo que era el suyo (el 0).
        var team = f.TeamStats.Single();
        Assert.Equal((38, 4, 9, 2, 5, 3, 1, 11, 14, 6, 4),
            (team.PossessionPct, team.ShotsFor, team.ShotsAgainst, team.ShotsOnTargetFor, team.ShotsOnTargetAgainst, team.CornersFor, team.CornersAgainst,
                team.ThrowInsFor, team.ThrowInsAgainst, team.GoalKicksFor, team.GoalKicksAgainst));
        Assert.Equal(f.Club.Id, team.RecordedByClubId);

        // Los dos tramos de Pelayo se suman en su línea, que se crea con los minutos que se le ha visto.
        var line = Assert.Single(f.Performances);
        Assert.Equal((f.Pelayo.Id, 20, false, 4, 2, 1, 6, 6, 25, 31),
            (line.SquadPlayerId, line.MinutesPlayed, line.Started, line.Shots, line.ShotsOnTarget, line.Goals, line.Recoveries, line.BallLosses, line.PassesCompleted, line.PassesAttempted));
        Assert.Equal(f.Club.Id, line.RecordedByClubId);
        Assert.NotNull(line.Rating);

        Assert.Equal((11, 1, 0), (result.TeamValuesWritten, result.LinesWritten, result.PlayersWithoutChanges));
        Assert.Equal(1, f.Saves);
        Assert.NotNull(result.Sheet.TeamStats);
        Assert.NotNull(result.Sheet.Entries.Single(e => e.Player.Id == f.Pelayo.Id).Performance);

        var part = Assert.Single(result.Analysis.Videos);
        Assert.Equal(0, part.OurTeam);
        Assert.NotNull(part.AppliedAt);
        Assert.Equal([f.Pelayo.Id, f.Pelayo.Id], part.Players.Where(p => p.Team == 0).Select(p => p.SquadPlayerId!.Value));
    }

    [Fact]
    public async Task Data_already_in_the_sheet_is_only_replaced_after_an_explicit_confirmation()
    {
        var f = new Fixture();
        f.Analyzed();
        f.Performances.Add(PlayerMatchPerformance.Record(f.Squad, f.Pelayo, f.Played, SquadEditor.Club(f.Club),
            new PerformanceStats(true, 90, 2, 1, 6, 3, 0, 2, 0, 0, 1, false, "Muy activo"), Now));
        f.TeamStats.Add(TeamMatchStats.Record(f.Squad, f.Played, SquadEditor.Club(f.Club),
            new TeamMatchStatsData(55, 3, 1, 4, 9, 2, 5, 0, 0, 12, 9, 0, 0, "Presión alta"), Now));

        var error = await Assert.ThrowsAsync<ConflictException>(() => f.Apply(f.ClubUser).Handle(f.Everything(), default));

        Assert.Equal(ErrorCode.VideoAnalysisOverwrite, error.Code);
        Assert.Equal("posesión (55 → 38), tiros de Pelayo Fernández (6 → 4), tiros a puerta de Pelayo Fernández (3 → 2)", error.Args.Single());
        Assert.Equal(0, f.Saves);
        Assert.Equal((55, 6, 2), (f.TeamStats.Single().PossessionPct, f.Performances.Single().Shots, f.Performances.Single().Goals));
        Assert.Null(f.Analysis.AppliedAt);

        await f.Apply(f.ClubUser).Handle(f.Everything() with { Overwrite = true }, default);

        var line = f.Performances.Single();
        // Lo que el vídeo no sabe (minutos, titular, asistencias, tarjetas, notas) se queda como estaba, y sus goles también.
        Assert.Equal((90, true, 2, 1, 1, "Muy activo"), (line.MinutesPlayed, line.Started, line.Goals, line.Assists, line.YellowCards, line.Notes));
        Assert.Equal((4, 2, 6, 25, 31), (line.Shots, line.ShotsOnTarget, line.Recoveries, line.PassesCompleted, line.PassesAttempted));
        Assert.Equal((38, 12, "Presión alta"), (f.TeamStats.Single().PossessionPct, f.TeamStats.Single().FoulsCommitted, f.TeamStats.Single().Notes));
        Assert.Equal(1, f.Saves);
    }

    [Fact]
    public async Task Only_what_is_ticked_is_written()
    {
        var f = new Fixture();
        f.Analyzed();

        var result = await f.Apply(f.ClubUser).Handle(
            new(f.Squad.Id, f.Played.Id, [new ApplyVideoPart(f.Video.Id, 1, [])], [VideoTeamField.Possession, VideoTeamField.Corners], false), default);

        var team = f.TeamStats.Single();
        Assert.Equal((62, 1, 3, 0, 0, 0), (team.PossessionPct, team.CornersFor, team.CornersAgainst, team.ShotsFor, team.ThrowInsFor, team.GoalKicksFor));
        Assert.Empty(f.Performances);
        Assert.Equal((3, 0), (result.TeamValuesWritten, result.LinesWritten));
        Assert.Equal(1, f.Analysis.OurTeam);
    }

    [Fact]
    public async Task A_tracked_player_must_exist_be_of_the_chosen_team_and_go_once()
    {
        var f = new Fixture();
        f.Analyzed();
        ApplyVideoAnalysisCommand With(params VideoTrackletAssignment[] players) =>
            new(f.Squad.Id, f.Played.Id, [new ApplyVideoPart(f.Video.Id, 0, players)], [], false);

        Assert.Equal(ErrorCode.VideoAnalysisTrackletUnknown,
            (await Assert.ThrowsAsync<DomainException>(() => f.Apply(f.ClubUser).Handle(With(new VideoTrackletAssignment(5, f.Pelayo.Id)), default))).Code);
        Assert.Equal(ErrorCode.VideoAnalysisTrackletOtherTeam,
            (await Assert.ThrowsAsync<DomainException>(() => f.Apply(f.ClubUser).Handle(With(new VideoTrackletAssignment(12, f.Pelayo.Id)), default))).Code);
        Assert.Equal(ErrorCode.VideoAnalysisTrackletRepeated,
            (await Assert.ThrowsAsync<DomainException>(() => f.Apply(f.ClubUser).Handle(With(new(696, f.Pelayo.Id), new(696, f.Pelayo.Id)), default))).Code);
        Assert.Equal(ErrorCode.VideoAnalysisNothingSelected,
            (await Assert.ThrowsAsync<DomainException>(() => f.Apply(f.ClubUser).Handle(With(), default))).Code);
        Assert.Equal(0, f.Saves);
        Assert.Empty(f.Performances);
    }

    [Fact]
    public async Task Nothing_is_applied_until_the_analysis_is_ready_or_for_a_match_not_played()
    {
        var f = new Fixture();
        f.Analyses.Items.Add(MatchVideoAnalysis.Queue(f.Video, Now));

        Assert.Equal(ErrorCode.VideoAnalysisNotReady,
            (await Assert.ThrowsAsync<ConflictException>(() => f.Apply(f.ClubUser).Handle(f.Everything(), default))).Code);
        Assert.Equal(0, f.Saves);
    }

    private sealed record User(Guid? PlayerId = null, Guid? CoachId = null, Guid? ClubId = null, Guid? UserId = null) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public AccountType? AccountType => Abstractions.Identity.AccountType.Club;
    }

    private sealed record Admin(Guid? UserId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public AccountType? AccountType => null;
        public Guid? PlayerId => null;
        public Guid? CoachId => null;
        public Guid? ClubId => null;
        public bool IsAdmin => true;
    }

    private sealed class Fixture
    {
        public Club Club { get; } = Club.Register(Guid.NewGuid(), "CD Vallobín", Now);
        public List<Club> OtherClubs { get; } = [];
        public Team Team { get; } = Team.Create("CD Vallobín", "VAL", null);
        public Team Rival { get; } = Team.Create("Real Titánico", "TIT", null);
        public Competition Competition { get; } = Competition.Create("Liga", "Primera", CompetitionLevel.Regional, 1, null, "Asturias", null);
        public Match Played { get; }
        public Squad Squad { get; }
        public SquadPlayer Pelayo { get; }
        public MatchVideo Video { get; }
        public MatchVideoAnalysis Analysis => Analyses.Items.Single();
        public FakeVideoStore Videos { get; } = new();
        public FakeAnalysisStore Analyses { get; } = new();
        public FakeAnalysisSignal Signal { get; } = new();
        public List<PlayerMatchPerformance> Performances { get; } = [];
        public List<TeamMatchStats> TeamStats { get; } = [];
        public User ClubUser => new(ClubId: Club.Id, UserId: Club.UserId);
        public int Saves { get; private set; }
        private readonly bool _enabled;

        public Fixture(bool enabled = true)
        {
            _enabled = enabled;
            var season = Competition.AddSeason("2026/2027", new(2026, 8, 1), new(2027, 6, 30), null);
            Competition.MarkCurrent(season.Id);
            Club.AddTeam(Team.Id, Now);
            Played = Match.Schedule(season.Id, 1, Team.Id, Rival.Id, Now.AddDays(-2), null, null);
            Played.RecordResult(2, 1, Now.AddDays(-2).AddHours(2));
            Squad = Squad.Open(Team.Id, season.Id, Now);
            Pelayo = Squad.AddPlayer("Pelayo Fernández", Position.Forward, 9, null, Now);
            Squad.AddPlayer("Nel Suárez", Position.Goalkeeper, 1, null, Now);

            Video = MatchVideo.Upload(Squad.Id, Team.Id, Played.Id, season.Id, null, VideoPart.Full, null, "partido.mp4", "video/mp4",
                50_000_000, "original.mp4", Now.AddHours(-3), Club.Id);
            Video.StartProcessing(Now.AddHours(-3));
            Video.KeepOriginal(5782, 1080, Now.AddHours(-2));
            Videos.Items.Add(Video);
            Analyses.Videos = Videos;
        }

        // El analizador ya ha pasado por el vídeo: propuesta lista y los recortes de dos de los tres jugadores.
        public void Analyzed()
        {
            var analysis = MatchVideoAnalysis.Queue(Video, Now.AddHours(-2));
            analysis.Start(Now.AddHours(-2));
            var run = VideoAnalysisSamples.Run();
            analysis.Complete(new VideoAnalysisOutcome(run.OutputPath, run.AnalyzerVersion, run.Weights, run.ProposalJson, run.Frames,
                run.BallFramesPct, run.CalibratedFramesPct, run.AnalyzedSeconds, run.PitchLengthM, run.PitchWidthM), Now.AddHours(-1));
            Analyses.Items.Add(analysis);
            Analyses.Crops[analysis.Id] = run.Crops.Select(c => new VideoAnalysisCrop(analysis.Id, c.Key, c.Value)).ToList();
        }

        // Nuestro equipo es el 0, sus dos jugadores seguidos son Pelayo y se aceptan todos los datos de equipo.
        public ApplyVideoAnalysisCommand Everything() => new(
            Squad.Id, Played.Id,
            [new ApplyVideoPart(Video.Id, 0, [new(696, Pelayo.Id), new(781, Pelayo.Id)])],
            Enum.GetValues<VideoTeamField>(), false);

        private ISquadStore SquadStore => TestStub.Of<ISquadStore>((method, args) => method switch
        {
            nameof(ISquadStore.GetByIdAsync) => Task.FromResult((Guid)args![0]! == Squad.Id ? Squad : null),
            _ => throw new NotSupportedException(method),
        });
        private IClubStore Clubs => TestStub.Of<IClubStore>((_, args) =>
            Task.FromResult(OtherClubs.Append(Club).FirstOrDefault(c => c.Id == (Guid)args![0]! || c.UserId == (Guid)args[0]!)));
        private ICoachStore Coaches => TestStub.Of<ICoachStore>((method, _) => throw new NotSupportedException(method));
        private IMatchStore Matches => TestStub.Of<IMatchStore>((method, args) => method switch
        {
            nameof(IMatchStore.GetByIdAsync) => Task.FromResult((Guid)args![0]! == Played.Id ? Played : null),
            _ => throw new NotSupportedException(method),
        });
        private ITeamStore Teams => TestStub.Of<ITeamStore>((method, args) => method switch
        {
            nameof(ITeamStore.GetByIdsAsync) => Task.FromResult<IReadOnlyList<Team>>([Team, Rival]),
            nameof(ITeamStore.GetByIdAsync) => Task.FromResult(new[] { Team, Rival }.FirstOrDefault(t => t.Id == (Guid)args![0]!)),
            _ => throw new NotSupportedException(method),
        });
        private ICompetitionStore Competitions => TestStub.Of<ICompetitionStore>((_, _) => Task.FromResult<Competition?>(Competition));
        private IPerformanceStore PerformanceStore => TestStub.Of<IPerformanceStore>((method, args) => method switch
        {
            nameof(IPerformanceStore.GetBySquadAndMatchAsync) => Task.FromResult<IReadOnlyList<PlayerMatchPerformance>>(
                Performances.Where(p => p.SquadId == (Guid)args![0]! && p.MatchId == (Guid)args[1]!).ToList()),
            nameof(IPerformanceStore.Add) => Run(() => Performances.Add((PlayerMatchPerformance)args![0]!)),
            _ => throw new NotSupportedException(method),
        });
        private IPlayerStore Players => TestStub.Of<IPlayerStore>((_, _) => Task.FromResult<IReadOnlyList<Player>>([]));
        private ITeamMatchStatsStore TeamStatsStore => TestStub.Of<ITeamMatchStatsStore>((method, args) => method switch
        {
            nameof(ITeamMatchStatsStore.GetAsync) => Task.FromResult(TeamStats.FirstOrDefault(s => s.SquadId == (Guid)args![0]! && s.MatchId == (Guid)args[1]!)),
            nameof(ITeamMatchStatsStore.Add) => Run(() => TeamStats.Add((TeamMatchStats)args![0]!)),
            _ => throw new NotSupportedException(method),
        });
        private IUnitOfWork Unit => TestStub.Of<IUnitOfWork>((_, _) => Task.FromResult(++Saves));
        private static object? Run(Action action) { action(); return null; }

        private VideoAnalysisReader Reader(ICurrentUser user) =>
            new(user, SquadStore, Clubs, Matches, Videos, Analyses, Options.Create(new VideoAnalysisOptions { Enabled = _enabled }), TimeProvider.System);

        public GetVideoAnalysisHandler Get(ICurrentUser user) => new(Reader(user));

        public RequestVideoAnalysisHandler Request(ICurrentUser user) => new(Reader(user), Analyses, Signal, Unit, TimeProvider.System);

        public GetVideoAnalysisCropHandler Crop(ICurrentUser user) => new(Reader(user), Analyses);

        public ApplyVideoAnalysisHandler Apply(ICurrentUser user) =>
            new(Reader(user), user, Coaches, Clubs, Teams, Competitions, Players, PerformanceStore, TeamStatsStore,
                new MatchSheetWriter(PerformanceStore, TeamStatsStore), Unit, TimeProvider.System);
    }
}
