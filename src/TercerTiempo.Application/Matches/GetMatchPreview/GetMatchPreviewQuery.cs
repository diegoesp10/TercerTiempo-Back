using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Standings;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Matches.GetMatchPreview;

public sealed record GetMatchPreviewQuery(Guid MatchId) : IQuery<MatchPreviewDto>;

public sealed record MatchPreviewDto(
    MatchDto Match,
    string CompetitionName,
    TeamPreviewDto Home,
    TeamPreviewDto Away,
    PredictionDto Prediction,
    IReadOnlyList<string> DataNotes);

public sealed record TeamPreviewDto(
    TeamRefDto Team,
    int? Position,
    int Played,
    int Won,
    int Drawn,
    int Lost,
    int GoalsFor,
    int GoalsAgainst,
    int Points,
    double PointsPerMatch,
    double GoalsForPerMatch,
    double GoalsAgainstPerMatch,
    VenueRecordDto AtVenue,
    IReadOnlyList<FormResultDto> Form,
    int OfficialSheets,
    IReadOnlyList<PreviewPlayerDto> InFormPlayers,
    SetPiecesDto SetPieces);

public sealed record VenueRecordDto(string Label, int Played, int Won, int Drawn, int Lost, int GoalsFor, int GoalsAgainst);

public sealed record FormResultDto(Guid MatchId, int Matchday, string OpponentName, bool Home, int GoalsFor, int GoalsAgainst, string Outcome);

public sealed record PreviewPlayerDto(
    string Name,
    int? ShirtNumber,
    int Matches,
    int Started,
    int Minutes,
    int Goals,
    int PenaltyGoals,
    int YellowCards,
    int RedCards,
    int GoalsLastThree,
    double FormScore);

public sealed record SetPiecesDto(int PenaltyGoalsFor, int PenaltyGoalsAgainst, int OwnGoalsFor, int OwnGoalsAgainst, int YellowCards, int RedCards);

public sealed record PredictionDto(
    double HomeWin,
    double Draw,
    double AwayWin,
    double ExpectedHomeGoals,
    double ExpectedAwayGoals,
    string MostLikelyScore,
    IReadOnlyList<string> Reasons);

internal sealed class GetMatchPreviewHandler(
    ICurrentUser currentUser,
    IMatchStore matches,
    ITeamStore teams,
    IStandingStore standings,
    ICompetitionStore competitions,
    IOfficialSheetStore sheets) : IQueryHandler<GetMatchPreviewQuery, MatchPreviewDto>
{
    private const int FormMatches = 5;
    private const int MaxGoals = 8;

    public async Task<MatchPreviewDto> Handle(GetMatchPreviewQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
            throw new UnauthenticatedException();
        // La previa es para quien prepara el partido: el cuerpo técnico y el club (sus dirigentes), siempre.
        if (currentUser.AccountType is not (AccountType.Coach or AccountType.Club))
            throw new ForbiddenException(ErrorCode.MatchPreviewStaffOnly);

        var match = await matches.GetByIdAsync(query.MatchId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.MatchNotFound, query.MatchId);

        var (competition, season) = await Resolvers.SeasonByIdAsync(competitions, match.SeasonId, cancellationToken);
        var seasonMatches = await matches.GetBySeasonAsync(season.Id, cancellationToken);
        var played = seasonMatches.Where(m => m.Status == MatchStatus.Played && m.Score is not null).OrderBy(m => m.KickoffAt).ToList();
        var table = (await standings.GetBySeasonAsync(season.Id, cancellationToken)).ToDictionary(s => s.TeamId);

        var teamIds = seasonMatches.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct();
        var teamMap = (await teams.GetByIdsAsync(teamIds, cancellationToken)).ToDictionary(t => t.Id);

        var league = LeagueAverages.From(played);
        var home = await BuildTeamAsync(match.HomeTeamId, isHome: true, match, played, table, teamMap, cancellationToken);
        var away = await BuildTeamAsync(match.AwayTeamId, isHome: false, match, played, table, teamMap, cancellationToken);
        var prediction = Predict(home, away, league, played, match);

        var notes = new List<string>
        {
            "Las probabilidades salen de los goles a favor y en contra de esta liga (modelo de Poisson con la fuerza atacante y defensiva de cada equipo y la ventaja de jugar en casa); con pocas jornadas disputadas son orientativas.",
            "Los jugadores en forma, los penaltis, los goles en propia y las tarjetas proceden de las actas públicas ya guardadas (federación o BeSoccer); los partidos sin acta no cuentan.",
            "Córners, posesión y goles a balón parado (salvo penaltis) no constan en ninguna fuente pública: los apunta el cuerpo técnico en «Datos del equipo» del acta interna de cada partido y solo se ven los de su propio equipo.",
        };

        return new MatchPreviewDto(MatchMapper.ToDto(match, teamMap), competition.Name, home, away, prediction, notes);
    }

    private async Task<TeamPreviewDto> BuildTeamAsync(
        Guid teamId, bool isHome, Match match, List<Match> played, Dictionary<Guid, Standing> table,
        Dictionary<Guid, Team> teamMap, CancellationToken cancellationToken)
    {
        var mine = played.Where(m => m.Involves(teamId)).ToList();
        var standing = table.GetValueOrDefault(teamId);

        var won = mine.Count(m => m.GoalsFor(teamId) > m.GoalsAgainst(teamId));
        var drawn = mine.Count(m => m.GoalsFor(teamId) == m.GoalsAgainst(teamId));
        var lost = mine.Count - won - drawn;
        var goalsFor = mine.Sum(m => m.GoalsFor(teamId) ?? 0);
        var goalsAgainst = mine.Sum(m => m.GoalsAgainst(teamId) ?? 0);
        var points = standing?.Points ?? won * 3 + drawn;
        var playedCount = standing?.Played ?? mine.Count;

        var venueMatches = mine.Where(m => m.IsHome(teamId) == isHome).ToList();
        var venue = new VenueRecordDto(
            isHome ? "en casa" : "fuera",
            venueMatches.Count,
            venueMatches.Count(m => m.GoalsFor(teamId) > m.GoalsAgainst(teamId)),
            venueMatches.Count(m => m.GoalsFor(teamId) == m.GoalsAgainst(teamId)),
            venueMatches.Count(m => m.GoalsFor(teamId) < m.GoalsAgainst(teamId)),
            venueMatches.Sum(m => m.GoalsFor(teamId) ?? 0),
            venueMatches.Sum(m => m.GoalsAgainst(teamId) ?? 0));

        var form = mine.TakeLast(FormMatches)
            .Select(m => new FormResultDto(
                m.Id, m.Matchday, teamMap.GetValueOrDefault(m.OpponentOf(teamId))?.Name ?? "Rival", m.IsHome(teamId),
                m.GoalsFor(teamId) ?? 0, m.GoalsAgainst(teamId) ?? 0, Outcome(m, teamId)))
            .ToList();

        var (players, setPieces, sheetsCount) = await PlayersFromSheetsAsync(teamId, mine, cancellationToken);

        return new TeamPreviewDto(
            MatchMapper.Ref(teamId, teamMap),
            standing?.Position,
            playedCount, standing?.Won ?? won, standing?.Drawn ?? drawn, standing?.Lost ?? lost,
            standing?.GoalsFor ?? goalsFor, standing?.GoalsAgainst ?? goalsAgainst, points,
            Rate(points, playedCount), Rate(standing?.GoalsFor ?? goalsFor, playedCount), Rate(standing?.GoalsAgainst ?? goalsAgainst, playedCount),
            venue, form, sheetsCount, players, setPieces);
    }

    private async Task<(IReadOnlyList<PreviewPlayerDto> Players, SetPiecesDto SetPieces, int Sheets)> PlayersFromSheetsAsync(
        Guid teamId, List<Match> mine, CancellationToken cancellationToken)
    {
        var available = (await sheets.GetByMatchIdsAsync(mine.Select(m => m.Id), cancellationToken))
            .Where(s => s.IsAvailable)
            .ToDictionary(s => s.MatchId);
        if (available.Count == 0)
            return ([], new SetPiecesDto(0, 0, 0, 0, 0, 0), 0);

        var ordered = mine.Where(m => available.ContainsKey(m.Id)).ToList();
        var lastThree = ordered.TakeLast(3).Select(m => m.Id).ToHashSet();
        var totals = new Dictionary<string, Accumulator>();
        int penFor = 0, penAgainst = 0, ownFor = 0, ownAgainst = 0, yellow = 0, red = 0;

        foreach (var m in ordered)
        {
            var sheet = available[m.Id];
            var side = m.IsHome(teamId) ? MatchSide.Home : MatchSide.Away;
            var other = side == MatchSide.Home ? MatchSide.Away : MatchSide.Home;

            penFor += sheet.LinesOf(side).Sum(l => l.PenaltyGoals);
            penAgainst += sheet.LinesOf(other).Sum(l => l.PenaltyGoals);
            ownFor += sheet.LinesOf(other).Sum(l => l.OwnGoals);
            ownAgainst += sheet.LinesOf(side).Sum(l => l.OwnGoals);

            foreach (var line in sheet.LinesOf(side))
            {
                if (!line.Played) continue;
                yellow += line.YellowCards;
                red += line.RedCard ? 1 : 0;

                if (!totals.TryGetValue(line.NormalizedName, out var acc))
                    totals[line.NormalizedName] = acc = new Accumulator(line.FullName);

                acc.ShirtNumber = line.ShirtNumber ?? acc.ShirtNumber;
                acc.Matches++;
                acc.Started += line.Started ? 1 : 0;
                acc.Minutes += line.MinutesPlayed;
                acc.Goals += line.Goals;
                acc.PenaltyGoals += line.PenaltyGoals;
                acc.Yellow += line.YellowCards;
                acc.Red += line.RedCard ? 1 : 0;
                if (lastThree.Contains(m.Id)) acc.GoalsLastThree += line.Goals;
            }
        }

        var players = totals.Values
            .Select(a => new PreviewPlayerDto(
                a.Name, a.ShirtNumber, a.Matches, a.Started, a.Minutes, a.Goals, a.PenaltyGoals, a.Yellow, a.Red, a.GoalsLastThree,
                Math.Round(a.Goals * 3 + a.GoalsLastThree * 2 + a.Minutes / 90.0 * 0.5 + a.Started * 0.3, 1)))
            .OrderByDescending(p => p.FormScore)
            .ThenByDescending(p => p.Minutes)
            .Take(8)
            .ToList();

        return (players, new SetPiecesDto(penFor, penAgainst, ownFor, ownAgainst, yellow, red), available.Count);
    }

    private static PredictionDto Predict(TeamPreviewDto home, TeamPreviewDto away, LeagueAverages league, List<Match> played, Match match)
    {
        // Fuerzas relativas a la media de la liga (1 = un equipo medio); sin partidos, equipo medio.
        var homeAttack = Strength(home.GoalsForPerMatch, league.GoalsPerTeamMatch);
        var homeDefence = Strength(home.GoalsAgainstPerMatch, league.GoalsPerTeamMatch);
        var awayAttack = Strength(away.GoalsForPerMatch, league.GoalsPerTeamMatch);
        var awayDefence = Strength(away.GoalsAgainstPerMatch, league.GoalsPerTeamMatch);

        var lambdaHome = Math.Max(0.15, league.HomeGoals * homeAttack * awayDefence);
        var lambdaAway = Math.Max(0.15, league.AwayGoals * awayAttack * homeDefence);

        double pHome = 0, pDraw = 0, pAway = 0, best = -1;
        var bestScore = "1-1";
        for (var h = 0; h <= MaxGoals; h++)
        {
            for (var a = 0; a <= MaxGoals; a++)
            {
                var p = Poisson(lambdaHome, h) * Poisson(lambdaAway, a);
                if (h > a) pHome += p; else if (h == a) pDraw += p; else pAway += p;
                if (p > best) { best = p; bestScore = $"{h}-{a}"; }
            }
        }
        var total = pHome + pDraw + pAway;

        var reasons = new List<string>();
        var homeName = home.Team.Name;
        var awayName = away.Team.Name;

        if (home.Played > 0 && away.Played > 0)
        {
            reasons.Add(home.PointsPerMatch >= away.PointsPerMatch
                ? $"{homeName} suma {home.PointsPerMatch:0.00} puntos por partido ({Ordinal(home.Position)}) frente a {away.PointsPerMatch:0.00} de {awayName} ({Ordinal(away.Position)})."
                : $"{awayName} suma {away.PointsPerMatch:0.00} puntos por partido ({Ordinal(away.Position)}) frente a {home.PointsPerMatch:0.00} de {homeName} ({Ordinal(home.Position)}).");

            reasons.Add(homeAttack >= awayAttack
                ? $"Ataque: {homeName} marca {home.GoalsForPerMatch:0.00} goles por partido ({Pct(homeAttack)} de la media de la liga) y {awayName} {away.GoalsForPerMatch:0.00}."
                : $"Ataque: {awayName} marca {away.GoalsForPerMatch:0.00} goles por partido ({Pct(awayAttack)} de la media de la liga) y {homeName} {home.GoalsForPerMatch:0.00}.");

            reasons.Add(homeDefence <= awayDefence
                ? $"Defensa: {homeName} encaja {home.GoalsAgainstPerMatch:0.00} por partido y {awayName} {away.GoalsAgainstPerMatch:0.00}."
                : $"Defensa: {awayName} encaja {away.GoalsAgainstPerMatch:0.00} por partido y {homeName} {home.GoalsAgainstPerMatch:0.00}.");
        }

        if (league.HomeWinRate > 0)
            reasons.Add($"En esta liga el local gana el {league.HomeWinRate:P0} de los partidos (media de {league.HomeGoals:0.00} goles el local y {league.AwayGoals:0.00} el visitante).");

        if (home.AtVenue.Played > 0)
            reasons.Add($"{homeName} en casa: {home.AtVenue.Won}V-{home.AtVenue.Drawn}E-{home.AtVenue.Lost}D, {home.AtVenue.GoalsFor}:{home.AtVenue.GoalsAgainst} en goles.");
        if (away.AtVenue.Played > 0)
            reasons.Add($"{awayName} fuera: {away.AtVenue.Won}V-{away.AtVenue.Drawn}E-{away.AtVenue.Lost}D, {away.AtVenue.GoalsFor}:{away.AtVenue.GoalsAgainst} en goles.");

        if (home.Form.Count > 0 || away.Form.Count > 0)
            reasons.Add($"Forma reciente (últimos {FormMatches}): {homeName} {FormLabel(home.Form)} · {awayName} {FormLabel(away.Form)}.");

        var previous = played.FirstOrDefault(m => m.Id != match.Id && m.Involves(match.HomeTeamId) && m.Involves(match.AwayTeamId));
        if (previous is not null)
            reasons.Add($"Precedente esta temporada (jornada {previous.Matchday}): {previous.Score!.Home}-{previous.Score.Away} con {(previous.HomeTeamId == match.HomeTeamId ? homeName : awayName)} de local.");

        var hot = away.InFormPlayers.FirstOrDefault();
        if (hot is not null && hot.Goals > 0)
            reasons.Add($"Peligro rival: {hot.Name} lleva {hot.Goals} goles ({hot.GoalsLastThree} en los tres últimos partidos con acta).");

        return new PredictionDto(
            Math.Round(pHome / total, 3), Math.Round(pDraw / total, 3), Math.Round(pAway / total, 3),
            Math.Round(lambdaHome, 2), Math.Round(lambdaAway, 2), bestScore, reasons);
    }

    private static double Strength(double perMatch, double leagueAverage) =>
        leagueAverage <= 0 || double.IsNaN(perMatch) ? 1 : Math.Clamp(perMatch / leagueAverage, 0.3, 3);

    private static double Poisson(double lambda, int k)
    {
        var result = Math.Exp(-lambda);
        for (var i = 1; i <= k; i++) result *= lambda / i;
        return result;
    }

    private static double Rate(int total, int played) => played == 0 ? 0 : Math.Round((double)total / played, 2);

    private static string Pct(double strength) => $"{strength:P0}";

    private static string Ordinal(int? position) => position is null ? "sin clasificar" : $"{position}º";

    private static string Outcome(Match m, Guid teamId) =>
        m.GoalsFor(teamId) > m.GoalsAgainst(teamId) ? "W" : m.GoalsFor(teamId) == m.GoalsAgainst(teamId) ? "D" : "L";

    private static string FormLabel(IReadOnlyList<FormResultDto> form) =>
        form.Count == 0 ? "sin partidos" : string.Concat(form.Select(f => f.Outcome switch { "W" => "V", "D" => "E", _ => "D" })) + $" ({form.Sum(f => f.Outcome switch { "W" => 3, "D" => 1, _ => 0 })} pts)";

    private sealed record LeagueAverages(double HomeGoals, double AwayGoals, double GoalsPerTeamMatch, double HomeWinRate)
    {
        public static LeagueAverages From(List<Match> played)
        {
            if (played.Count == 0) return new LeagueAverages(1.4, 1.1, 1.25, 0);
            var homeGoals = played.Sum(m => m.Score!.Home);
            var awayGoals = played.Sum(m => m.Score!.Away);
            return new LeagueAverages(
                (double)homeGoals / played.Count,
                (double)awayGoals / played.Count,
                (double)(homeGoals + awayGoals) / (played.Count * 2),
                (double)played.Count(m => m.Score!.Home > m.Score.Away) / played.Count);
        }
    }

    private sealed class Accumulator(string name)
    {
        public string Name { get; } = name;
        public int? ShirtNumber { get; set; }
        public int Matches { get; set; }
        public int Started { get; set; }
        public int Minutes { get; set; }
        public int Goals { get; set; }
        public int PenaltyGoals { get; set; }
        public int Yellow { get; set; }
        public int Red { get; set; }
        public int GoalsLastThree { get; set; }
    }
}
