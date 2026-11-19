using TercerTiempo.Domain.Common;
using TercerTiempo.Application.Teams;
using TercerTiempo.Application.Licensing;
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
    IReadOnlyList<string> DataNotes,
    [property: System.ComponentModel.Description("Hasta cuándo cuentan los datos: el inicio del partido. Solo entra lo jugado antes (la previa de la jornada 1 no sabe nada de la 10).")]
    DateTimeOffset? AsOf = null,
    [property: System.ComponentModel.Description("Partidos de la liga jugados antes de este, en los que se basa la previa.")]
    int LeagueMatchesBefore = 0,
    [property: System.ComponentModel.Description("true si la clasificación se ha calculado con los resultados anteriores al partido (partido ya jugado o con jornadas posteriores ya disputadas); false si es la oficial de hoy.")]
    bool TableFromResults = false,
    [property: System.ComponentModel.Description("Enfrentamientos anteriores entre los dos equipos, de esta temporada y de las pasadas que estén guardadas.")]
    HeadToHeadDto? HeadToHead = null);

public sealed record HeadToHeadDto(
    int Played,
    [property: System.ComponentModel.Description("Victorias del local de este partido (jugara donde jugara aquellos).")]
    int HomeWins,
    int Draws,
    int AwayWins,
    int HomeGoals,
    int AwayGoals,
    [property: System.ComponentModel.Description("Temporadas distintas con algún enfrentamiento.")]
    int Seasons,
    [property: System.ComponentModel.Description("Del más reciente al más antiguo.")]
    IReadOnlyList<HeadToHeadMatchDto> Matches);

public sealed record HeadToHeadMatchDto(
    Guid MatchId,
    string SeasonLabel,
    string CompetitionName,
    int Matchday,
    DateTimeOffset KickoffAt,
    TeamRefDto HomeTeam,
    TeamRefDto AwayTeam,
    int HomeGoals,
    int AwayGoals,
    [property: System.ComponentModel.Description("Resultado para el local de este partido: W, D o L.")]
    string Outcome,
    [property: System.ComponentModel.Description("De la misma temporada y liga que este partido.")]
    bool SameSeason);

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

public sealed record FormResultDto(
    Guid MatchId, int Matchday, string OpponentName, bool Home, int GoalsFor, int GoalsAgainst, string Outcome, Guid OpponentId, string? OpponentCrestUrl);

public sealed record PreviewPlayerDto(
    string Name,
    [property: System.ComponentModel.Description("El dorsal que más ha llevado en los partidos que ha jugado, según las actas oficiales.")]
    int? ShirtNumber,
    int Matches,
    int Started,
    int Minutes,
    int Goals,
    int PenaltyGoals,
    int YellowCards,
    int RedCards,
    int GoalsLastThree,
    double FormScore,
    [property: System.ComponentModel.Description("Foto de su ficha en la plantilla del equipo (mismo nombre), si la tiene.")]
    string? PhotoUrl);

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
    IOfficialSheetStore sheets,
    ILicenseGate licenses) : IQueryHandler<GetMatchPreviewQuery, MatchPreviewDto>
{
    private const int FormMatches = 5;
    private const int MaxGoals = 8;

    public async Task<MatchPreviewDto> Handle(GetMatchPreviewQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
            throw new UnauthenticatedException();
        // La previa es para quien prepara el partido: el cuerpo técnico y el club (sus dirigentes), siempre. El
        // administrador también la ve.
        if (currentUser.AccountType is not (AccountType.Coach or AccountType.Club) && !currentUser.IsAdmin)
            throw new ForbiddenException(ErrorCode.MatchPreviewStaffOnly);
        // Y solo con un equipo con licencia vigente.
        if (!await licenses.IsViewerLicensedAsync(currentUser, cancellationToken))
            throw new ForbiddenException(ErrorCode.LicenseViewerRequired);

        var match = await matches.GetByIdAsync(query.MatchId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.MatchNotFound, query.MatchId);

        var (competition, season) = await Resolvers.SeasonByIdAsync(competitions, match.SeasonId, cancellationToken);
        var seasonMatches = await matches.GetBySeasonAsync(season.Id, cancellationToken);

        // La previa se hace con lo que se sabía antes del partido: solo los jugados antes de su inicio. Así la de la jornada
        // 1 no usa lo que pasó en la 10, y la de un partido ya jugado es la que se habría visto ese día.
        var asOf = match.KickoffAt;
        var allPlayed = seasonMatches.Where(m => m.Id != match.Id && m.Status == MatchStatus.Played && m.Score is not null).ToList();
        var played = allPlayed.Where(m => m.KickoffAt < asOf).OrderBy(m => m.KickoffAt).ToList();

        // La clasificación oficial es la de hoy (con sanciones, si las hay): solo vale si el partido está por jugar y no hay
        // ninguno jugado después de su inicio. Si no, se calcula con los resultados anteriores.
        var official = (await standings.GetBySeasonAsync(season.Id, cancellationToken)).ToDictionary(s => s.TeamId);
        var tableFromResults = match.Status == MatchStatus.Played || allPlayed.Count != played.Count || official.Count == 0;
        var table = tableFromResults ? TableAt(played) : official.ToDictionary(kv => kv.Key, kv => TableRow.From(kv.Value));

        var teamIds = seasonMatches.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct();
        var teamMap = (await teams.GetByIdsAsync(teamIds, cancellationToken)).ToDictionary(t => t.Id);

        var league = LeagueAverages.From(played);
        var home = await BuildTeamAsync(match.HomeTeamId, isHome: true, match, played, table, teamMap, cancellationToken);
        var away = await BuildTeamAsync(match.AwayTeamId, isHome: false, match, played, table, teamMap, cancellationToken);
        var headToHead = await HeadToHeadAsync(match, asOf, teamMap, cancellationToken);
        var prediction = Predict(home, away, league, headToHead, match);

        var notes = new List<string>
        {
            played.Count == 0
                ? $"Datos hasta el inicio del partido ({SpainTime.Local(asOf):dd/MM/yyyy HH:mm}): todavía no se había jugado ningún partido de la liga, así que las probabilidades parten de un equipo medio con la ventaja de jugar en casa."
                : $"Datos hasta el inicio del partido ({SpainTime.Local(asOf):dd/MM/yyyy HH:mm}): {played.Count} partidos de la liga jugados antes; lo posterior no cuenta.{(tableFromResults ? " La clasificación se ha calculado con esos resultados (sin sanciones de despacho)." : string.Empty)}",
            "Las probabilidades salen de los goles a favor y en contra de esta liga (modelo de Poisson con la fuerza atacante y defensiva de cada equipo y la ventaja de jugar en casa); con pocas jornadas disputadas son orientativas.",
            "Los jugadores en forma, los penaltis, los goles en propia y las tarjetas proceden de las actas públicas de la federación ya guardadas; los partidos sin acta no cuentan.",
            "Córners, posesión y goles a balón parado (salvo penaltis) no constan en ninguna fuente pública: los apunta el cuerpo técnico en «Datos del equipo» del acta interna de cada partido y solo se ven los de su propio equipo.",
        };

        if (headToHead.Played > 0)
            notes.Add($"Enfrentamientos anteriores: {headToHead.Played} partidos guardados de {headToHead.Seasons} {(headToHead.Seasons == 1 ? "temporada" : "temporadas")} (las temporadas pasadas que no se han descargado no cuentan).");

        return new MatchPreviewDto(MatchMapper.ToDto(match, teamMap), competition.Name, home, away, prediction, notes, asOf, played.Count, tableFromResults, headToHead);
    }

    // Los enfrentamientos entre los dos equipos jugados antes de este, de cualquier temporada y liga guardada.
    private async Task<HeadToHeadDto> HeadToHeadAsync(Match match, DateTimeOffset asOf, Dictionary<Guid, Team> teamMap, CancellationToken cancellationToken)
    {
        var meetings = (await matches.GetBetweenAsync(match.HomeTeamId, match.AwayTeamId, cancellationToken))
            .Where(m => m.Id != match.Id && m.Status == MatchStatus.Played && m.Score is not null && m.KickoffAt < asOf)
            .OrderByDescending(m => m.KickoffAt)
            .ToList();

        var seasons = new Dictionary<Guid, (string Label, string Competition)>();
        foreach (var seasonId in meetings.Select(m => m.SeasonId).Distinct())
        {
            var (competition, season) = await Resolvers.SeasonByIdAsync(competitions, seasonId, cancellationToken);
            seasons[seasonId] = (season.Label, competition.Name);
        }

        var rows = meetings.Select(m => new HeadToHeadMatchDto(
            m.Id, seasons[m.SeasonId].Label, seasons[m.SeasonId].Competition, m.Matchday, m.KickoffAt,
            MatchMapper.Ref(m.HomeTeamId, teamMap), MatchMapper.Ref(m.AwayTeamId, teamMap),
            m.Score!.Home, m.Score.Away, Outcome(m, match.HomeTeamId), m.SeasonId == match.SeasonId)).ToList();

        return new HeadToHeadDto(
            rows.Count,
            rows.Count(r => r.Outcome == "W"), rows.Count(r => r.Outcome == "D"), rows.Count(r => r.Outcome == "L"),
            meetings.Sum(m => m.GoalsFor(match.HomeTeamId) ?? 0), meetings.Sum(m => m.GoalsAgainst(match.HomeTeamId) ?? 0),
            seasons.Count, rows);
    }

    // Clasificación con los resultados dados: 3 puntos la victoria y 1 el empate; a igualdad, diferencia de goles y goles a
    // favor. Solo entran los equipos que ya han jugado.
    private static Dictionary<Guid, TableRow> TableAt(List<Match> played)
    {
        var rows = played
            .SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId })
            .Distinct()
            .Select(teamId =>
            {
                var mine = played.Where(m => m.Involves(teamId)).ToList();
                var won = mine.Count(m => m.GoalsFor(teamId) > m.GoalsAgainst(teamId));
                var drawn = mine.Count(m => m.GoalsFor(teamId) == m.GoalsAgainst(teamId));
                return new TableRow(teamId, 0, mine.Count, won, drawn, mine.Count - won - drawn,
                    mine.Sum(m => m.GoalsFor(teamId) ?? 0), mine.Sum(m => m.GoalsAgainst(teamId) ?? 0), won * 3 + drawn);
            })
            .OrderByDescending(r => r.Points).ThenByDescending(r => r.GoalsFor - r.GoalsAgainst).ThenByDescending(r => r.GoalsFor)
            .ToList();
        return rows.Select((r, i) => r with { Position = i + 1 }).ToDictionary(r => r.TeamId);
    }

    private sealed record TableRow(Guid TeamId, int Position, int Played, int Won, int Drawn, int Lost, int GoalsFor, int GoalsAgainst, int Points)
    {
        public static TableRow From(Standing s) => new(s.TeamId, s.Position, s.Played, s.Won, s.Drawn, s.Lost, s.GoalsFor, s.GoalsAgainst, s.Points);
    }

    private async Task<TeamPreviewDto> BuildTeamAsync(
        Guid teamId, bool isHome, Match match, List<Match> played, Dictionary<Guid, TableRow> table,
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
                m.GoalsFor(teamId) ?? 0, m.GoalsAgainst(teamId) ?? 0, Outcome(m, teamId),
                m.OpponentOf(teamId), teamMap.GetValueOrDefault(m.OpponentOf(teamId))?.CrestLink()))
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
        var available = (await sheets.GetAvailableLinesByMatchIdsAsync(mine.Select(m => m.Id), cancellationToken))
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

                if (line.ShirtNumber is { } number) acc.Worn.Add((number, m.KickoffAt));
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

        // Las actas solo traen nombres: la foto sale de la ficha de la plantilla con el mismo nombre.
        var photos = (await teams.GetRosterAsync(teamId, ordered[0].SeasonId, cancellationToken))
            .Where(e => e.PhotoKey is not null)
            .GroupBy(e => e.NormalizedName)
            .ToDictionary(g => g.Key, g => g.First().PhotoLink());

        var players = totals.Values
            .Select(a => new PreviewPlayerDto(
                a.Name, Teams.Roster.ShirtNumberUsage.From(a.Worn).FirstOrDefault()?.Number, a.Matches, a.Started, a.Minutes, a.Goals, a.PenaltyGoals, a.Yellow, a.Red, a.GoalsLastThree,
                Math.Round(a.Goals * 3 + a.GoalsLastThree * 2 + a.Minutes / 90.0 * 0.5 + a.Started * 0.3, 1),
                photos.GetValueOrDefault(NameNormalizer.Normalize(a.Name))))
            .OrderByDescending(p => p.FormScore)
            .ThenByDescending(p => p.Minutes)
            .Take(8)
            .ToList();

        return (players, new SetPiecesDto(penFor, penAgainst, ownFor, ownAgainst, yellow, red), available.Count);
    }

    private static PredictionDto Predict(TeamPreviewDto home, TeamPreviewDto away, LeagueAverages league, HeadToHeadDto headToHead, Match match)
    {
        // Fuerzas relativas a la media de la liga (1 = un equipo medio); sin partidos, equipo medio.
        var homeAttack = Strength(home.GoalsForPerMatch, home.Played, league.GoalsPerTeamMatch);
        var homeDefence = Strength(home.GoalsAgainstPerMatch, home.Played, league.GoalsPerTeamMatch);
        var awayAttack = Strength(away.GoalsForPerMatch, away.Played, league.GoalsPerTeamMatch);
        var awayDefence = Strength(away.GoalsAgainstPerMatch, away.Played, league.GoalsPerTeamMatch);

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

        if (headToHead.Matches.FirstOrDefault() is { } last)
        {
            var record = headToHead.Played == 1
                ? "un enfrentamiento anterior"
                : $"{headToHead.Played} enfrentamientos anteriores ({homeName} {headToHead.HomeWins} {(headToHead.HomeWins == 1 ? "victoria" : "victorias")}, {headToHead.Draws} {(headToHead.Draws == 1 ? "empate" : "empates")}, {awayName} {headToHead.AwayWins})";
            reasons.Add($"Precedentes: {record}; el último, {(last.SameSeason ? "esta temporada" : last.SeasonLabel)} (jornada {last.Matchday}): {last.HomeTeam.Name} {last.HomeGoals}-{last.AwayGoals} {last.AwayTeam.Name}.");
        }

        var hot = away.InFormPlayers.FirstOrDefault();
        if (hot is not null && hot.Goals > 0)
            reasons.Add($"Peligro rival: {hot.Name} lleva {hot.Goals} goles ({hot.GoalsLastThree} en los tres últimos partidos con acta).");

        return new PredictionDto(
            Math.Round(pHome / total, 3), Math.Round(pDraw / total, 3), Math.Round(pAway / total, 3),
            Math.Round(lambdaHome, 2), Math.Round(lambdaAway, 2), bestScore, reasons);
    }

    // Con pocos partidos la fuerza se acerca a la de un equipo medio (como si llevara TeamPriorMatches partidos de media):
    // sin jugar es un equipo medio y tras una goleada en la jornada 1 no pasa a ser el triple de bueno.
    private const double TeamPriorMatches = 3;

    private static double Strength(double perMatch, int played, double leagueAverage)
    {
        if (leagueAverage <= 0 || double.IsNaN(perMatch)) return 1;
        var shrunk = (perMatch * played + leagueAverage * TeamPriorMatches) / (played + TeamPriorMatches);
        return Math.Clamp(shrunk / leagueAverage, 0.3, 3);
    }

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
        // Las primeras jornadas dicen poco de la liga: las medias parten de las de una liga normal (1,4 goles el local y
        // 1,1 el visitante, con el peso de PriorMatches partidos) y se van ajustando a lo jugado.
        private const double PriorHomeGoals = 1.4;
        private const double PriorAwayGoals = 1.1;
        private const double PriorMatches = 10;

        public static LeagueAverages From(List<Match> played)
        {
            var homeGoals = (played.Sum(m => m.Score!.Home) + PriorHomeGoals * PriorMatches) / (played.Count + PriorMatches);
            var awayGoals = (played.Sum(m => m.Score!.Away) + PriorAwayGoals * PriorMatches) / (played.Count + PriorMatches);
            var homeWinRate = played.Count == 0 ? 0 : (double)played.Count(m => m.Score!.Home > m.Score.Away) / played.Count;
            return new LeagueAverages(homeGoals, awayGoals, (homeGoals + awayGoals) / 2, homeWinRate);
        }
    }

    private sealed class Accumulator(string name)
    {
        public string Name { get; } = name;
        public List<(int Number, DateTimeOffset KickoffAt)> Worn { get; } = [];
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
