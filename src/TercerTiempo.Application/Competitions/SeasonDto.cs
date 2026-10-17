using TercerTiempo.Domain.Competitions;

namespace TercerTiempo.Application.Competitions;

public sealed record SeasonDto(
    Guid Id,
    string Label,
    Guid CompetitionId,
    string CompetitionName,
    string Region,
    DateOnly StartsOn,
    DateOnly EndsOn)
{
    public static SeasonDto From(Competition competition, Season season) =>
        new(season.Id, season.Label, competition.Id, competition.Name, competition.Region, season.StartsOn, season.EndsOn);
}
