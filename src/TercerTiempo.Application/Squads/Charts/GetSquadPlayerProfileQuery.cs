using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Matches;
using TercerTiempo.Application.Performances;
using TercerTiempo.Application.Stats;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Squads.Charts;

/// <summary>Ficha de un jugador de la plantilla para sus gráficos: serie partido a partido, perfil por 90' y referencias.</summary>
public sealed record GetSquadPlayerProfileQuery(Guid SquadId, Guid SquadPlayerId) : IQuery<SquadPlayerProfileDto>;

internal sealed class GetSquadPlayerProfileHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IMatchStore matches,
    ITeamStore teams,
    ICompetitionStore competitions,
    IPlayerStore players,
    IPerformanceStore performances,
    TimeProvider clock) : IQueryHandler<GetSquadPlayerProfileQuery, SquadPlayerProfileDto>
{
    public async Task<SquadPlayerProfileDto> Handle(GetSquadPlayerProfileQuery query, CancellationToken cancellationToken)
    {
        var squad = await squads.GetByIdAsync(query.SquadId, cancellationToken)
            ?? throw new NotFoundException("Plantilla", query.SquadId);

        SquadAccess.EnsureCanView(squad, currentUser, clock);

        var entry = squad.Players.FirstOrDefault(p => p.Id == query.SquadPlayerId)
            ?? throw new NotFoundException("Jugador", query.SquadPlayerId);

        var summary = await SquadMapper.ToSummaryAsync(squad, currentUser.CoachId, clock, teams, competitions, cancellationToken);

        var teamMatches = (await matches.GetTeamMatchesAsync(squad.SeasonId, squad.TeamId, cancellationToken))
            .Where(m => m.Status == MatchStatus.Played)
            .OrderBy(m => m.KickoffAt)
            .ToList();

        var all = await performances.GetBySquadAsync(squad.Id, cancellationToken);
        var byPlayer = all.ToLookup(p => p.SquadPlayerId);
        var mine = byPlayer[entry.Id].ToList();
        var mineByMatch = mine.ToDictionary(p => p.MatchId);

        var stats = PlayerStatsCalculator.Individual(mine);
        var impact = PlayerStatsCalculator.TeamImpact(squad.TeamId, teamMatches, mine);

        // Referencias de la plantilla: jugadores con minutos suficientes para que su perfil por 90' tenga sentido.
        var reference = squad.Players
            .Select(p => byPlayer[p.Id].ToList())
            .Where(list => list.Sum(l => l.MinutesPlayed) >= PlayerAxes.MinMinutesForReference)
            .Select(list => PlayerAxes.From(PlayerStatsCalculator.Individual(list), PlayerStatsCalculator.TeamImpact(squad.TeamId, teamMatches, list)))
            .ToList();

        var teamIds = teamMatches.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct();
        var teamMap = (await teams.GetByIdsAsync(teamIds, cancellationToken)).ToDictionary(t => t.Id);

        var linked = entry.PlayerId is { } playerId
            ? (await players.GetByIdsAsync([playerId], cancellationToken)).ToDictionary(p => p.Id)
            : null;

        var points = teamMatches
            .Select(m =>
            {
                mineByMatch.TryGetValue(m.Id, out var performance);
                var opponent = MatchMapper.Ref(m.OpponentOf(squad.TeamId), teamMap);
                var breakdown = performance?.Breakdown(entry.Position, m);

                return new PlayerMatchPointDto(
                    MatchMapper.ToDto(m, teamMap),
                    m.IsHome(squad.TeamId),
                    opponent.Name,
                    opponent.ShortName,
                    m.GoalsFor(squad.TeamId),
                    m.GoalsAgainst(squad.TeamId),
                    m.OutcomeFor(squad.TeamId),
                    performance is null ? null : PerformanceMapper.ToDto(performance, entry),
                    breakdown is null
                        ? null
                        : new RatingBreakdownDto(breakdown.Attack, breakdown.Creation, breakdown.Defense, breakdown.Goalkeeping,
                            breakdown.Discipline, breakdown.Result, breakdown.Total));
            })
            .ToList();

        return new SquadPlayerProfileDto(
            summary,
            SquadMapper.ToPlayerDto(entry, linked),
            stats,
            impact,
            PlayerAxes.From(stats, impact),
            PlayerAxes.Average(reference),
            PlayerAxes.Max(reference),
            points);
    }
}
