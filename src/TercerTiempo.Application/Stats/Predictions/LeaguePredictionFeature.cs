using System.ComponentModel;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Competitions;
using TercerTiempo.Application.Seasons;
using TercerTiempo.Application.Teams;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Stats.Predictions;

public sealed record LeagueZonesDto(
    [property: Description("Puestos que cuentan como ascenso o promoción.")]
    IReadOnlyList<int> Promotion,
    [property: Description("Puestos de descenso; vacío si la liga no tiene.")]
    IReadOnlyList<int> Relegation,
    int Teams,
    [property: Description("true si son las plazas que publica la web de la liga; false si son unas aproximadas por categoría.")]
    bool Published,
    [property: Description("Los puestos destacados con su nombre y color, tal como los publica la web (vacío si no los publica).")]
    IReadOnlyList<LeagueZoneDto> Places);

public sealed record TeamPredictionDto(
    Guid TeamId,
    string TeamName,
    string ShortName,
    string? CrestUrl,
    [property: Description("Puesto, puntos y partidos en la jornada elegida.")]
    int Position,
    int Points,
    int Played,
    int GoalDifference,
    [property: Description("Probabilidad (0-1) de acabar primero.")]
    double Champion,
    [property: Description("Probabilidad (0-1) de acabar en ascenso o promoción.")]
    double Promotion,
    [property: Description("Probabilidad (0-1) de acabar en descenso; 0 si no hay descenso.")]
    double Relegation,
    double ExpectedPoints,
    double ExpectedPosition,
    [property: Description("Probabilidad de acabar en cada puesto, del 1.º al último.")]
    IReadOnlyList<double> PositionOdds,
    [property: Description("Fuerza estimada: ataque (más de 1, marca más que la media) y defensa (menos de 1, encaja menos).")]
    double Attack,
    double Defense);

public sealed record LeaguePredictionDto(
    SeasonDto? Season,
    [property: Description("Jornada elegida: solo cuentan los resultados hasta ella.")]
    int? Matchday,
    [property: Description("Jornadas con resultados que se pueden elegir.")]
    IReadOnlyList<int> Matchdays,
    int TotalMatchdays,
    [property: Description("Partidos con resultado hasta la jornada y partidos que se simulan.")]
    int KnownMatches,
    int SimulatedMatches,
    int Simulations,
    LeagueZonesDto? Zones,
    [property: Description("Equipos ordenados por el puesto final esperado.")]
    IReadOnlyList<TeamPredictionDto> Teams,
    string? Notice);

public sealed record TeamEvolutionDto(
    Guid TeamId,
    string TeamName,
    string ShortName,
    string? CrestUrl,
    [property: Description("Una por jornada de Matchdays.")]
    IReadOnlyList<double> Champion,
    IReadOnlyList<double> Promotion,
    IReadOnlyList<double> Relegation,
    IReadOnlyList<int> Position);

public sealed record LeaguePredictionEvolutionDto(
    IReadOnlyList<int> Matchdays,
    int Simulations,
    LeagueZonesDto? Zones,
    IReadOnlyList<TeamEvolutionDto> Teams);

public sealed record GetLeaguePredictionQuery(Guid CompetitionId, int? Matchday) : IQuery<LeaguePredictionDto>;

public sealed record GetLeaguePredictionEvolutionQuery(Guid CompetitionId) : IQuery<LeaguePredictionEvolutionDto>;

// Lo que el previsor necesita de una liga en la temporada elegida: sus partidos, sus equipos y una huella de los datos para
// la caché (cambia en cuanto se sincroniza un resultado o una fecha).
internal sealed record ForecastSource(
    Competition Competition,
    Season Season,
    IReadOnlyList<Match> Matches,
    IReadOnlyList<Guid> TeamIds,
    IReadOnlyDictionary<Guid, Team> Teams,
    IReadOnlyList<int> PlayedMatchdays,
    ForecastZones Zones,
    IReadOnlyList<SeasonZone> Places,
    string Fingerprint)
{
    public int TotalMatchdays => Matches.Count == 0 ? 0 : Matches.Max(m => m.Matchday);

    // Lo jugado hasta la jornada lleva su resultado; todo lo demás (también lo jugado después) se simula. Los partidos
    // cancelados no cuentan.
    public IReadOnlyList<ForecastMatch> AsOf(int matchday) =>
        Matches
            .Where(m => m.Status != MatchStatus.Cancelled)
            .Select(m => m.Status == MatchStatus.Played && m.Matchday <= matchday && m.Score is { } score
                ? new ForecastMatch(m.HomeTeamId, m.AwayTeamId, score.Home, score.Away)
                : new ForecastMatch(m.HomeTeamId, m.AwayTeamId, null, null))
            .ToList();

    public int Seed(int matchday) => BitConverter.ToInt32(Season.Id.ToByteArray(), 0) ^ (matchday * 7919);

    public LeagueZonesDto ZonesDto => new(Zones.Promotion, Zones.Relegation, TeamIds.Count, Places.Count > 0, Places.Select(LeagueZoneDto.From).ToList());
}

internal sealed class ForecastSources(
    ICompetitionStore competitions, IMatchStore matches, ITeamStore teams, ISeasonZoneStore zones, ISeasonContext seasons)
{
    public async Task<ForecastSource?> LoadAsync(Guid competitionId, CancellationToken cancellationToken)
    {
        var competition = await competitions.GetByIdAsync(competitionId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.CompetitionNotFound, competitionId);
        var season = Resolvers.SeasonFor(competition, await seasons.SelectedAsync(cancellationToken));
        if (season is null) return null;

        var list = (await matches.GetBySeasonAsync(season.Id, cancellationToken)).OrderBy(m => m.Id).ToList();
        var teamIds = list.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct().OrderBy(id => id).ToList();
        var teamMap = (await teams.GetByIdsAsync(teamIds, cancellationToken)).ToDictionary(t => t.Id);
        var playedMatchdays = list.Where(m => m.Status == MatchStatus.Played && m.Score is not null).Select(m => m.Matchday).Distinct().Order().ToList();

        var places = await zones.GetBySeasonAsync(season.Id, cancellationToken);
        var hash = new HashCode();
        foreach (var place in places)
        {
            hash.Add(place.Position);
            hash.Add(place.Kind);
        }
        foreach (var m in list)
        {
            hash.Add(m.Id);
            hash.Add(m.Matchday);
            hash.Add(m.Status);
            hash.Add(m.Score?.Home);
            hash.Add(m.Score?.Away);
        }

        var forecastZones = places.Count > 0 ? LeagueZoneRules.From(places) : LeagueZoneRules.For(competition, teamIds.Count);
        return new ForecastSource(
            competition, season, list, teamIds, teamMap, playedMatchdays, forecastZones, places, $"{season.Id:N}:{hash.ToHashCode()}");
    }
}

internal sealed class GetLeaguePredictionHandler(ForecastSources sources, LeagueForecastCache cache)
    : IQueryHandler<GetLeaguePredictionQuery, LeaguePredictionDto>
{
    public const int Simulations = 10_000;

    public async Task<LeaguePredictionDto> Handle(GetLeaguePredictionQuery query, CancellationToken cancellationToken)
    {
        var source = await sources.LoadAsync(query.CompetitionId, cancellationToken);
        if (source is null)
            return new LeaguePredictionDto(null, null, [], 0, 0, 0, 0, null, [], "Esta liga no se jugó en la temporada elegida.");
        if (source.PlayedMatchdays.Count == 0)
            return new LeaguePredictionDto(
                SeasonDto.From(source.Competition, source.Season), null, [], source.TotalMatchdays, 0, source.Matches.Count, 0, source.ZonesDto, [],
                "El previsor empieza en cuanto haya resultados de la primera jornada.");

        var matchday = query.Matchday is { } wanted
            ? source.PlayedMatchdays.LastOrDefault(d => d <= wanted, source.PlayedMatchdays[0])
            : source.PlayedMatchdays[^1];
        var asOf = source.AsOf(matchday);
        var forecast = cache.GetOrAdd($"{source.Fingerprint}:{matchday}:{Simulations}", () =>
            LeagueForecaster.Run(source.TeamIds, asOf, source.Zones, Simulations, source.Seed(matchday)));

        var teams = forecast.Select(f =>
        {
            var team = source.Teams.GetValueOrDefault(f.TeamId);
            return new TeamPredictionDto(
                f.TeamId, team?.Name ?? "Equipo", team?.ShortName ?? "???", team?.CrestLink(), f.Position, f.Points, f.Played, f.GoalsFor - f.GoalsAgainst,
                f.Champion, f.Promotion, f.Relegation, f.ExpectedPoints, f.ExpectedPosition, f.PositionOdds, f.Attack, f.Defense);
        }).ToList();

        var known = asOf.Count(m => m.HomeGoals is not null);
        return new LeaguePredictionDto(
            SeasonDto.From(source.Competition, source.Season), matchday, source.PlayedMatchdays, source.TotalMatchdays, known, asOf.Count - known,
            Simulations, source.ZonesDto, teams, null);
    }
}

internal sealed class GetLeaguePredictionEvolutionHandler(ForecastSources sources, LeagueForecastCache cache)
    : IQueryHandler<GetLeaguePredictionEvolutionQuery, LeaguePredictionEvolutionDto>
{
    public const int Simulations = 3_000;

    public async Task<LeaguePredictionEvolutionDto> Handle(GetLeaguePredictionEvolutionQuery query, CancellationToken cancellationToken)
    {
        var source = await sources.LoadAsync(query.CompetitionId, cancellationToken);
        if (source is null || source.PlayedMatchdays.Count == 0) return new LeaguePredictionEvolutionDto([], 0, source?.ZonesDto, []);

        var byMatchday = cache.GetOrAdd($"{source.Fingerprint}:evolution:{Simulations}", () =>
            source.PlayedMatchdays
                .AsParallel().AsOrdered()
                .Select(day => LeagueForecaster.Run(source.TeamIds, source.AsOf(day), source.Zones, Simulations, source.Seed(day)).ToDictionary(f => f.TeamId))
                .ToList());

        var last = byMatchday[^1];
        var teams = source.TeamIds
            .OrderBy(id => last[id].ExpectedPosition)
            .Select(id =>
            {
                var team = source.Teams.GetValueOrDefault(id);
                return new TeamEvolutionDto(
                    id, team?.Name ?? "Equipo", team?.ShortName ?? "???", team?.CrestLink(),
                    byMatchday.Select(d => d[id].Champion).ToList(), byMatchday.Select(d => d[id].Promotion).ToList(),
                    byMatchday.Select(d => d[id].Relegation).ToList(), byMatchday.Select(d => d[id].Position).ToList());
            })
            .ToList();
        return new LeaguePredictionEvolutionDto(source.PlayedMatchdays, Simulations, source.ZonesDto, teams);
    }
}
