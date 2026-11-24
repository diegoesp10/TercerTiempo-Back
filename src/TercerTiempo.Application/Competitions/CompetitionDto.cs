using TercerTiempo.Domain.Competitions;

namespace TercerTiempo.Application.Competitions;

public sealed record CompetitionDto(
    Guid Id,
    string Name,
    string Category,
    CompetitionLevel Level,
    int Tier,
    string? Group,
    string Region,
    TerritoryDto Territory,
    int SortOrder,
    Guid? SeasonId,
    string? SeasonLabel,
    int TeamsCount)
{
    public static CompetitionDto From(Competition competition, TerritoryDto territory, Season? season, int teamsCount) =>
        new(competition.Id, competition.Name, competition.Category, competition.Level, competition.Tier, competition.Group,
            competition.Region, territory, competition.SortOrder, season?.Id, season?.Label, teamsCount);
}
