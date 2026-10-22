using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Application.Squads;
using TercerTiempo.Application.Squads.Charts;
using TercerTiempo.Application.Squads.Injuries;
using TercerTiempo.Application.Videos;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Repository.Squads;

public interface ISquadsRepository
{
    Task<IReadOnlyList<SquadSummaryDto>> ListMineAsync(CancellationToken cancellationToken = default);
    Task<SquadDetailDto> GetAsync(Guid squadId, CancellationToken cancellationToken = default);

    Task<SquadPlayerDto> AddPlayerAsync(Guid squadId, SquadPlayerRequest request, CancellationToken cancellationToken = default);
    Task<SquadPlayerDto> UpdatePlayerAsync(Guid squadId, Guid squadPlayerId, SquadPlayerRequest request, CancellationToken cancellationToken = default);
    Task<SquadPlayerDto> RemovePlayerAsync(Guid squadId, Guid squadPlayerId, CancellationToken cancellationToken = default);
    Task<SquadDetailDto> ImportRosterAsync(Guid squadId, CancellationToken cancellationToken = default);

    Task<SquadDetailDto> SetVisibilityAsync(Guid squadId, SquadVisibilityRequest request, CancellationToken cancellationToken = default);
    Task<SquadDetailDto> GeneratePlayerInviteCodeAsync(Guid squadId, Guid squadPlayerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SquadMatchDto>> ListMatchesAsync(Guid squadId, CancellationToken cancellationToken = default);
    Task<MatchSheetDto> GetMatchSheetAsync(Guid squadId, Guid matchId, CancellationToken cancellationToken = default);
    Task<MatchSheetDto> RecordMatchSheetAsync(Guid squadId, Guid matchId, MatchSheetRequest request, CancellationToken cancellationToken = default);
    Task<SheetComparisonDto> CompareMatchSheetAsync(Guid squadId, Guid matchId, bool refresh, CancellationToken cancellationToken = default);
    Task<MatchVideoDto> UploadVideoAsync(Guid squadId, Guid matchId, VideoPart part, string? title, VideoUpload file, CancellationToken cancellationToken = default);
    Task<VideoUploadDto> BeginVideoUploadAsync(Guid squadId, Guid matchId, BeginVideoUploadRequest request, CancellationToken cancellationToken = default);
    Task<VideoUploadDto> GetVideoUploadAsync(Guid squadId, Guid uploadId, CancellationToken cancellationToken = default);
    Task<VideoUploadDto> AppendVideoChunkAsync(Guid squadId, Guid uploadId, int chunkIndex, long length, Stream content, CancellationToken cancellationToken = default);
    Task<MatchVideoDto> CompleteVideoUploadAsync(Guid squadId, Guid uploadId, CancellationToken cancellationToken = default);
    Task<bool> CancelVideoUploadAsync(Guid squadId, Guid uploadId, CancellationToken cancellationToken = default);
    Task<SquadStatsDto> GetStatsAsync(Guid squadId, CancellationToken cancellationToken = default);

    Task<SquadPlayerProfileDto> GetPlayerProfileAsync(Guid squadId, Guid squadPlayerId, CancellationToken cancellationToken = default);
    Task<SquadTimelineDto> GetTimelineAsync(Guid squadId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InjuryTypeDto>> ListInjuryTypesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InjuryDto>> ListInjuriesAsync(Guid squadId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InjuryDto>> RecordInjuryAsync(Guid squadId, InjuryRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InjuryDto>> UpdateInjuryAsync(Guid squadId, Guid injuryId, InjuryRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InjuryDto>> CloseInjuryAsync(Guid squadId, Guid injuryId, CloseInjuryRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InjuryDto>> ReopenInjuryAsync(Guid squadId, Guid injuryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InjuryDto>> DeleteInjuryAsync(Guid squadId, Guid injuryId, CancellationToken cancellationToken = default);
}
