using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Matches;
using TercerTiempo.Application.Matches.GetMatch;

namespace TercerTiempo.Repository.Matches;

internal sealed class MatchesRepository(IQueryHandler<GetMatchQuery, MatchDetailDto> getMatch) : IMatchesRepository
{
    public Task<MatchDetailDto> GetAsync(Guid matchId, CancellationToken cancellationToken = default) =>
        getMatch.Handle(new GetMatchQuery(matchId), cancellationToken);
}
