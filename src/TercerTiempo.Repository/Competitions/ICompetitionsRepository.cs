using TercerTiempo.Application.Competitions;
using TercerTiempo.Application.Competitions.Matchdays;

namespace TercerTiempo.Repository.Competitions;

public interface ICompetitionsRepository
{
    Task<IReadOnlyList<CompetitionDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<CompetitionMatchdayDto> GetMatchdayAsync(Guid competitionId, int? matchday, CancellationToken cancellationToken = default);
}
