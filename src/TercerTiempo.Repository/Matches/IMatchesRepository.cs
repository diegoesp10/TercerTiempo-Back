using TercerTiempo.Application.Matches;
using TercerTiempo.Application.Matches.GetMatchPreview;
using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Application.Videos;

namespace TercerTiempo.Repository.Matches;

/// <summary>Detalle de partidos: actas visibles para el usuario, acta oficial de la federación y vídeos.</summary>
public interface IMatchesRepository
{
    Task<MatchDetailDto> GetAsync(Guid matchId, CancellationToken cancellationToken = default);
    Task<MatchPreviewDto> GetPreviewAsync(Guid matchId, CancellationToken cancellationToken = default);
    Task<OfficialSheetDto> GetOfficialSheetAsync(Guid matchId, bool refresh, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MatchVideoDto>> ListVideosAsync(Guid matchId, CancellationToken cancellationToken = default);
}
