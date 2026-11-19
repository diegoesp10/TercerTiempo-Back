using System.Reflection;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Matches.GetMatchPreview;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Standings;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Tests;

// La previa de un partido solo sabe lo jugado antes de su inicio (la de la jornada 1 no puede tener los datos de la 10) y
// trae los enfrentamientos anteriores entre los dos equipos, también de temporadas pasadas.
public class MatchPreviewTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 6, 17, 0, 0, TimeSpan.Zero);

    private readonly Team _vallobin = Team.Create("CD Vallobín", "VAL", null);
    private readonly Team _candas = Team.Create("Candás CF", "CAN", null);
    private readonly Team _third = Team.Create("CD Tineo", "TIN", null);
    private readonly Team _fourth = Team.Create("CD Covadonga", "COV", null);
    private readonly Competition _competition = Competition.Create("1ª AsturFútbol", "1ª AsturFútbol", CompetitionLevel.Regional, 1, null, "Asturias", null);
    private readonly Competition _lower = Competition.Create("2ª AsturFútbol", "2ª AsturFútbol", CompetitionLevel.Regional, 2, null, "Asturias", null);
    private readonly Season _season;
    private readonly Season _pastSeason;
    private readonly List<Match> _matches = [];
    private readonly List<Standing> _standings = [];

    public MatchPreviewTests()
    {
        _season = _competition.AddSeason("2026/2027", new DateOnly(2026, 8, 1), new DateOnly(2027, 6, 30), null);
        _pastSeason = _lower.AddSeason("2025/2026", new DateOnly(2025, 8, 1), new DateOnly(2026, 6, 30), null);
    }

    [Fact]
    public async Task The_first_matchday_preview_knows_nothing_of_what_was_played_after_it()
    {
        var first = Scheduled(1, _vallobin, _candas, 0);
        first.RecordResult(1, 0, Start);
        _matches.AddRange([first, Played(2, _candas, _third, 7, 4, 0), Played(3, _third, _vallobin, 14, 0, 3)]);
        // La clasificación oficial de hoy ya tiene tres jornadas: no puede salir en la previa de la primera.
        _standings.Add(Standing.Create(_season.Id, _vallobin.Id, new StandingLine(1, 2, 2, 0, 0, 4, 0, 6), Start.AddDays(15)));

        var preview = await Handler().Handle(new GetMatchPreviewQuery(first.Id), default);

        Assert.Equal(0, preview.LeagueMatchesBefore);
        Assert.Equal(Start, preview.AsOf);
        Assert.True(preview.TableFromResults);
        Assert.Equal((0, 0, (int?)null), (preview.Home.Played, preview.Home.Points, preview.Home.Position));
        Assert.Empty(preview.Home.Form);
        Assert.Equal(0, preview.HeadToHead!.Played);
    }

    [Fact]
    public void Without_matches_both_teams_are_average_and_the_home_side_is_slight_favourite()
    {
        // Dos 0-0 antes del partido no convierten la previa en un empate casi seguro.
        _matches.AddRange([Played(1, _third, _fourth, -1, 0, 0), Played(1, _fourth, _third, -1, 0, 0)]);
        var first = Scheduled(1, _vallobin, _candas, 0);
        _matches.Add(first);

        var preview = Handler().Handle(new GetMatchPreviewQuery(first.Id), default).GetAwaiter().GetResult();

        Assert.True(preview.Prediction.HomeWin > preview.Prediction.AwayWin);
        Assert.True(preview.Prediction.HomeWin > preview.Prediction.Draw);
        Assert.InRange(preview.Prediction.Draw, 0.2, 0.35);
    }

    [Fact]
    public async Task A_later_matchday_counts_only_the_earlier_results_with_the_table_at_that_date()
    {
        _matches.AddRange([
            Played(1, _vallobin, _third, 0, 2, 0),
            Played(1, _candas, _third, -1, 1, 1),
            Played(2, _third, _candas, 7, 0, 3),
        ]);
        var third = Scheduled(3, _vallobin, _candas, 14);
        third.RecordResult(0, 2, Start.AddDays(14));
        _matches.AddRange([third, Played(4, _candas, _vallobin, 21, 1, 1)]);

        var preview = await Handler().Handle(new GetMatchPreviewQuery(third.Id), default);

        Assert.Equal(3, preview.LeagueMatchesBefore);
        Assert.True(preview.TableFromResults);
        // Candás: empate y victoria (4 puntos, 1º); Vallobín: una victoria (3 puntos, 2º). La jornada 4 no cuenta.
        Assert.Equal((1, 3, 2), (preview.Home.Played, preview.Home.Points, preview.Home.Position!.Value));
        Assert.Equal((2, 4, 1), (preview.Away.Played, preview.Away.Points, preview.Away.Position!.Value));
        Assert.Equal(["W"], preview.Home.Form.Select(f => f.Outcome));
        Assert.Equal(0, preview.HeadToHead!.Played);
    }

    [Fact]
    public async Task An_upcoming_match_with_nothing_played_after_it_uses_the_official_table()
    {
        _matches.Add(Played(1, _vallobin, _third, -7, 2, 0));
        var next = Scheduled(2, _candas, _vallobin, 7);
        _matches.Add(next);
        _standings.Add(Standing.Create(_season.Id, _vallobin.Id, new StandingLine(1, 1, 1, 0, 0, 2, 0, 2), Start));

        var preview = await Handler().Handle(new GetMatchPreviewQuery(next.Id), default);

        Assert.False(preview.TableFromResults);
        // Los puntos oficiales (con una sanción de un punto) mandan sobre los calculados.
        Assert.Equal(2, preview.Away.Points);
    }

    [Fact]
    public async Task The_head_to_head_brings_earlier_meetings_from_this_and_past_seasons_from_the_home_side()
    {
        var pastHome = Match.Schedule(_pastSeason.Id, 10, _candas.Id, _vallobin.Id, Start.AddDays(-200), null, null);
        pastHome.RecordResult(2, 1, Start.AddDays(-200));
        var pastAway = Match.Schedule(_pastSeason.Id, 25, _vallobin.Id, _candas.Id, Start.AddDays(-90), null, null);
        pastAway.RecordResult(1, 1, Start.AddDays(-90));
        var earlier = Played(1, _vallobin, _candas, 0, 3, 0);
        var target = Scheduled(15, _candas, _vallobin, 100);
        var later = Played(20, _vallobin, _candas, 150, 0, 1);
        _matches.AddRange([earlier, target, later]);
        _between.AddRange([pastHome, pastAway, earlier, target, later]);

        var preview = await Handler().Handle(new GetMatchPreviewQuery(target.Id), default);

        var h2h = preview.HeadToHead!;
        // Desde el Candás (local de este partido): ganó en casa la temporada pasada, empató y perdió 3-0 en la jornada 1.
        Assert.Equal((3, 1, 1, 1, 3, 5, 2), (h2h.Played, h2h.HomeWins, h2h.Draws, h2h.AwayWins, h2h.HomeGoals, h2h.AwayGoals, h2h.Seasons));
        Assert.Equal([earlier.Id, pastAway.Id, pastHome.Id], h2h.Matches.Select(m => m.MatchId));
        Assert.Equal(["L", "D", "W"], h2h.Matches.Select(m => m.Outcome));
        Assert.Equal([true, false, false], h2h.Matches.Select(m => m.SameSeason));
        Assert.Equal(("2025/2026", "2ª AsturFútbol"), (h2h.Matches[1].SeasonLabel, h2h.Matches[1].CompetitionName));
        Assert.Contains(preview.Prediction.Reasons, r => r.StartsWith("Precedentes: 3 enfrentamientos anteriores", StringComparison.Ordinal));
    }

    private readonly List<Match> _between = [];

    private Match Scheduled(int matchday, Team home, Team away, int days)
    {
        var match = Match.Schedule(_season.Id, matchday, home.Id, away.Id, Start.AddDays(days), null, null);
        return match;
    }

    private Match Played(int matchday, Team home, Team away, int days, int homeGoals, int awayGoals)
    {
        var match = Scheduled(matchday, home, away, days);
        match.RecordResult(homeGoals, awayGoals, Start.AddDays(days));
        return match;
    }

    private GetMatchPreviewHandler Handler()
    {
        var teams = new[] { _vallobin, _candas, _third, _fourth };
        return new GetMatchPreviewHandler(
            new Coach(),
            Stub<IMatchStore>((method, args) => method switch
            {
                nameof(IMatchStore.GetByIdAsync) => Task.FromResult(_matches.Concat(_between).FirstOrDefault(m => m.Id == (Guid)args![0]!)),
                nameof(IMatchStore.GetBySeasonAsync) => Task.FromResult<IReadOnlyList<Match>>(_matches.Where(m => m.SeasonId == (Guid)args![0]!).ToList()),
                nameof(IMatchStore.GetBetweenAsync) => Task.FromResult<IReadOnlyList<Match>>(_between.OrderBy(m => m.KickoffAt).ToList()),
                _ => throw new NotSupportedException(method),
            }),
            Stub<ITeamStore>((method, _) => method == nameof(ITeamStore.GetByIdsAsync)
                ? Task.FromResult<IReadOnlyList<Team>>(teams)
                : throw new NotSupportedException(method)),
            Stub<IStandingStore>((method, args) => method == nameof(IStandingStore.GetBySeasonAsync)
                ? Task.FromResult<IReadOnlyList<Standing>>(_standings.Where(s => s.SeasonId == (Guid)args![0]!).ToList())
                : throw new NotSupportedException(method)),
            Stub<ICompetitionStore>((method, args) => method == nameof(ICompetitionStore.GetBySeasonAsync)
                ? Task.FromResult<Competition?>(new[] { _competition, _lower }.First(c => c.Seasons.Any(s => s.Id == (Guid)args![0]!)))
                : throw new NotSupportedException(method)),
            Stub<IOfficialSheetStore>((method, _) => method == nameof(IOfficialSheetStore.GetAvailableLinesByMatchIdsAsync)
                ? Task.FromResult<IReadOnlyList<OfficialMatchSheet>>([])
                : throw new NotSupportedException(method)),
            Stub<TercerTiempo.Application.Licensing.ILicenseGate>((_, _) => Task.FromResult(true)));
    }

    private sealed record Coach : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public AccountType? AccountType => Abstractions.Identity.AccountType.Coach;
        public Guid? UserId { get; } = Guid.NewGuid();
        public Guid? PlayerId => null;
        public Guid? CoachId { get; } = Guid.NewGuid();
        public Guid? ClubId => null;
    }

    private static T Stub<T>(Func<string, object?[]?, object?> call) where T : class
    {
        var result = DispatchProxy.Create<T, PlayerInvitationTests.StoreProxy>();
        ((PlayerInvitationTests.StoreProxy)(object)result).Call = call;
        return result;
    }
}
