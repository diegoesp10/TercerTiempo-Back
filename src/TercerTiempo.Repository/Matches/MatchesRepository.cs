using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Matches;
using TercerTiempo.Application.Matches.GetMatch;
using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Application.Videos;

namespace TercerTiempo.Repository.Matches;

internal sealed class MatchesRepository(
    IQueryHandler<GetMatchQuery, MatchDetailDto> getMatch,
    ICommandHandler<SyncOfficialSheetCommand, OfficialSheetDto> syncOfficialSheet,
    IQueryHandler<ListMatchVideosQuery, IReadOnlyList<MatchVideoDto>> listVideos) : IMatchesRepository
{
    public Task<MatchDetailDto> GetAsync(Guid matchId, CancellationToken cancellationToken = default) =>
        getMatch.Handle(new GetMatchQuery(matchId), cancellationToken);

    public Task<OfficialSheetDto> GetOfficialSheetAsync(Guid matchId, bool refresh, CancellationToken cancellationToken = default) =>
        syncOfficialSheet.Handle(new SyncOfficialSheetCommand(matchId, refresh), cancellationToken);

    public Task<IReadOnlyList<MatchVideoDto>> ListVideosAsync(Guid matchId, CancellationToken cancellationToken = default) =>
        listVideos.Handle(new ListMatchVideosQuery(matchId), cancellationToken);
}
