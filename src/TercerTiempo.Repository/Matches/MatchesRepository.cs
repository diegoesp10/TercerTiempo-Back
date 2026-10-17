using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Matches;
using TercerTiempo.Application.Matches.GetMatch;
using TercerTiempo.Application.Performances;
using TercerTiempo.Application.Performances.LogPerformance;

namespace TercerTiempo.Repository.Matches;

internal sealed class MatchesRepository(
    IQueryHandler<GetMatchQuery, MatchDetailDto> getMatch,
    ICommandHandler<LogPerformanceCommand, PerformanceDto> logPerformance) : IMatchesRepository
{
    public Task<MatchDetailDto> GetAsync(Guid matchId, CancellationToken cancellationToken = default) =>
        getMatch.Handle(new GetMatchQuery(matchId), cancellationToken);

    public Task<PerformanceDto> LogPerformanceAsync(Guid matchId, LogPerformanceRequest request, CancellationToken cancellationToken = default) =>
        logPerformance.Handle(request.ToCommand(matchId), cancellationToken);
}
