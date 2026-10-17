using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Performances;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Matches.GetMatch;

/// <summary>Detalle de un partido con los rendimientos registrados por los jugadores de la plataforma.</summary>
public sealed record GetMatchQuery(Guid MatchId) : IQuery<MatchDetailDto>;

internal sealed class GetMatchHandler(
    ICurrentUser currentUser,
    IMatchStore matches,
    ITeamStore teams,
    IPerformanceStore performances,
    IPlayerStore players) : IQueryHandler<GetMatchQuery, MatchDetailDto>
{
    public async Task<MatchDetailDto> Handle(GetMatchQuery query, CancellationToken cancellationToken)
    {
        var match = await matches.GetByIdAsync(query.MatchId, cancellationToken)
            ?? throw new NotFoundException("Partido", query.MatchId);

        var teamMap = (await teams.GetByIdsAsync([match.HomeTeamId, match.AwayTeamId], cancellationToken))
            .ToDictionary(t => t.Id);

        var matchPerformances = await performances.GetByMatchAsync(match.Id, cancellationToken);
        var playerMap = (await players.GetByIdsAsync(matchPerformances.Select(p => p.PlayerId), cancellationToken))
            .ToDictionary(p => p.Id);

        var dtos = matchPerformances
            .Select(p => PerformanceMapper.ToDto(p, playerMap.GetValueOrDefault(p.PlayerId)))
            .OrderByDescending(p => p.MinutesPlayed)
            .ToList();

        var mine = currentUser.PlayerId is { } playerId
            ? dtos.FirstOrDefault(p => p.PlayerId == playerId)
            : null;

        return new MatchDetailDto(MatchMapper.ToDto(match, teamMap, mine?.Id), mine, dtos);
    }
}
