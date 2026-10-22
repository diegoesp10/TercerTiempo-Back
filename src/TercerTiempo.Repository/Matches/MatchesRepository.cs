using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Matches;
using TercerTiempo.Application.Matches.GetMatch;
using TercerTiempo.Application.Matches.GetMatchPreview;
using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Application.Videos;

namespace TercerTiempo.Repository.Matches;

internal sealed class MatchesRepository(
    IQueryHandler<GetMatchPreviewQuery, MatchPreviewDto> getPreview,
    IQueryHandler<GetMatchQuery, MatchDetailDto> getMatch,
    ICommandHandler<SyncOfficialSheetCommand, OfficialSheetDto> syncOfficialSheet,
    IQueryHandler<GetOfficialSheetQuery, OfficialSheetDto> getOfficialSheet,
    IQueryHandler<ListMatchVideosQuery, IReadOnlyList<MatchVideoDto>> listVideos) : IMatchesRepository
{
    public Task<MatchPreviewDto> GetPreviewAsync(Guid matchId, CancellationToken cancellationToken = default) =>
        getPreview.Handle(new GetMatchPreviewQuery(matchId), cancellationToken);

    public Task<MatchDetailDto> GetAsync(Guid matchId, CancellationToken cancellationToken = default) =>
        getMatch.Handle(new GetMatchQuery(matchId), cancellationToken);

    public Task<OfficialSheetDto> GetOfficialSheetAsync(Guid matchId, bool refresh, CancellationToken cancellationToken = default) =>
        refresh
            ? syncOfficialSheet.Handle(new SyncOfficialSheetCommand(matchId, Force: true), cancellationToken)
            : getOfficialSheet.Handle(new GetOfficialSheetQuery(matchId), cancellationToken);

    public Task<IReadOnlyList<MatchVideoDto>> ListVideosAsync(Guid matchId, CancellationToken cancellationToken = default) =>
        listVideos.Handle(new ListMatchVideosQuery(matchId), cancellationToken);
}
