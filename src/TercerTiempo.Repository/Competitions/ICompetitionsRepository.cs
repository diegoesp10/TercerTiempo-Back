using TercerTiempo.Application.Competitions;
using TercerTiempo.Application.Competitions.Matchdays;
using TercerTiempo.Application.Seasons;

namespace TercerTiempo.Repository.Competitions;

public interface ICompetitionsRepository
{
    Task<IReadOnlyList<CompetitionDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SeasonYearDto>> ListSeasonsAsync(CancellationToken cancellationToken = default);

    Task<CompetitionMatchdayDto> GetMatchdayAsync(Guid competitionId, int? matchday, CancellationToken cancellationToken = default);
}
