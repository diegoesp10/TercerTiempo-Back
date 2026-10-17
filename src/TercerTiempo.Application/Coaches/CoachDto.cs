using TercerTiempo.Application.Squads;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Coaches;

public sealed record CoachDto(Guid Id, string DisplayName, IReadOnlyList<SquadSummaryDto> Squads);

internal static class CoachMapper
{
    public static async Task<CoachDto> ToDtoAsync(
        Coach coach, ISquadStore squads, ITeamStore teams, ICompetitionStore competitions, CancellationToken cancellationToken)
    {
        var mine = await squads.GetByCoachAsync(coach.Id, cancellationToken);
        var summaries = new List<SquadSummaryDto>(mine.Count);

        foreach (var squad in mine)
            summaries.Add(await SquadMapper.ToSummaryAsync(squad, coach.Id, teams, competitions, cancellationToken));

        return new CoachDto(coach.Id, coach.DisplayName, summaries);
    }
}
