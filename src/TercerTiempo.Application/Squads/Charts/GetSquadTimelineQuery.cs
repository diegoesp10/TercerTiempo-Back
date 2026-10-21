using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Matches;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Squads.Charts;

/// <summary>Evolución del equipo de la plantilla partido a partido: resultado, puntos acumulados y nota media del acta.</summary>
public sealed record GetSquadTimelineQuery(Guid SquadId) : IQuery<SquadTimelineDto>;

internal sealed class GetSquadTimelineHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IMatchStore matches,
    ITeamStore teams,
    ICompetitionStore competitions,
    IPerformanceStore performances,
    TimeProvider clock) : IQueryHandler<GetSquadTimelineQuery, SquadTimelineDto>
{
    public async Task<SquadTimelineDto> Handle(GetSquadTimelineQuery query, CancellationToken cancellationToken)
    {
        var squad = await squads.GetByIdAsync(query.SquadId, cancellationToken)
            ?? throw new NotFoundException("Plantilla", query.SquadId);

        SquadAccess.EnsureCanView(squad, currentUser, clock);

        var summary = await SquadMapper.ToSummaryAsync(squad, currentUser.CoachId, clock, teams, competitions, cancellationToken);

        var played = (await matches.GetTeamMatchesAsync(squad.SeasonId, squad.TeamId, cancellationToken))
            .Where(m => m.Status == MatchStatus.Played)
            .OrderBy(m => m.KickoffAt)
            .ToList();

        var byMatch = (await performances.GetBySquadAsync(squad.Id, cancellationToken)).ToLookup(p => p.MatchId);
        var names = squad.Players.ToDictionary(p => p.Id, p => p.FullName);

        var teamIds = played.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct();
        var teamMap = (await teams.GetByIdsAsync(teamIds, cancellationToken)).ToDictionary(t => t.Id);

        var cumulative = 0;
        var points = new List<SquadTimelinePointDto>(played.Count);

        foreach (var match in played)
        {
            var matchPoints = match.PointsFor(squad.TeamId) ?? 0;
            cumulative += matchPoints;

            var lines = byMatch[match.Id].ToList();
            var rated = lines.Where(l => l.Rating is not null).ToList();
            var best = rated.OrderByDescending(l => l.Rating).FirstOrDefault();
            var opponent = MatchMapper.Ref(match.OpponentOf(squad.TeamId), teamMap);

            points.Add(new SquadTimelinePointDto(
                MatchMapper.ToDto(match, teamMap),
                match.IsHome(squad.TeamId),
                opponent.Name,
                opponent.ShortName,
                match.GoalsFor(squad.TeamId) ?? 0,
                match.GoalsAgainst(squad.TeamId) ?? 0,
                match.OutcomeFor(squad.TeamId),
                matchPoints,
                cumulative,
                lines.Count > 0,
                lines.Count(l => l.MinutesPlayed > 0),
                rated.Count > 0 ? Math.Round(rated.Average(l => l.Rating!.Value), 2) : null,
                best is null ? null : names.GetValueOrDefault(best.SquadPlayerId),
                best?.Rating));
        }

        return new SquadTimelineDto(summary, points);
    }
}
