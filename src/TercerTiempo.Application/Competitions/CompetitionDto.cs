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
    int SortOrder,
    Guid? CurrentSeasonId,
    string? CurrentSeasonLabel,
    int TeamsCount)
{
    public static CompetitionDto From(Competition competition, int teamsCount) =>
        new(competition.Id, competition.Name, competition.Category, competition.Level, competition.Tier, competition.Group,
            competition.Region, competition.SortOrder, competition.CurrentSeason?.Id, competition.CurrentSeason?.Label, teamsCount);
}
