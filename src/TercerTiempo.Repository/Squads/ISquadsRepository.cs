using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Application.Squads;
using TercerTiempo.Application.Squads.Charts;
using TercerTiempo.Application.Videos;

namespace TercerTiempo.Repository.Squads;

/// <summary>Gestión de plantillas por el cuerpo técnico: jugadores, visibilidad, actas, comparador, vídeos y estadísticas.</summary>
public interface ISquadsRepository
{
    Task<IReadOnlyList<SquadSummaryDto>> ListMineAsync(CancellationToken cancellationToken = default);
    Task<SquadDetailDto> GetAsync(Guid squadId, CancellationToken cancellationToken = default);

    Task<SquadPlayerDto> AddPlayerAsync(Guid squadId, SquadPlayerRequest request, CancellationToken cancellationToken = default);
    Task<SquadPlayerDto> UpdatePlayerAsync(Guid squadId, Guid squadPlayerId, SquadPlayerRequest request, CancellationToken cancellationToken = default);
    Task<SquadPlayerDto> RemovePlayerAsync(Guid squadId, Guid squadPlayerId, CancellationToken cancellationToken = default);
    Task<SquadDetailDto> ImportRosterAsync(Guid squadId, CancellationToken cancellationToken = default);

    Task<SquadDetailDto> SetVisibilityAsync(Guid squadId, SquadVisibilityRequest request, CancellationToken cancellationToken = default);
    Task<SquadDetailDto> RotateInviteCodeAsync(Guid squadId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SquadMatchDto>> ListMatchesAsync(Guid squadId, CancellationToken cancellationToken = default);
    Task<MatchSheetDto> GetMatchSheetAsync(Guid squadId, Guid matchId, CancellationToken cancellationToken = default);
    Task<MatchSheetDto> RecordMatchSheetAsync(Guid squadId, Guid matchId, MatchSheetRequest request, CancellationToken cancellationToken = default);
    Task<SheetComparisonDto> CompareMatchSheetAsync(Guid squadId, Guid matchId, bool refresh, CancellationToken cancellationToken = default);
    Task<MatchVideoDto> UploadVideoAsync(Guid squadId, Guid matchId, string? title, VideoUpload file, CancellationToken cancellationToken = default);
    Task<SquadStatsDto> GetStatsAsync(Guid squadId, CancellationToken cancellationToken = default);

    Task<SquadPlayerProfileDto> GetPlayerProfileAsync(Guid squadId, Guid squadPlayerId, CancellationToken cancellationToken = default);
    Task<SquadTimelineDto> GetTimelineAsync(Guid squadId, CancellationToken cancellationToken = default);
}
