using TercerTiempo.Domain.Standings;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Standings;

public sealed record StandingDto(
    int Position,
    Guid TeamId,
    string TeamName,
    string TeamShortName,
    string? CrestUrl,
    int Played,
    int Won,
    int Drawn,
    int Lost,
    int GoalsFor,
    int GoalsAgainst,
    int GoalDifference,
    int Points)
{
    public static StandingDto From(Standing standing, Team? team) =>
        new(standing.Position, standing.TeamId,
            team?.Name ?? "Equipo desconocido", team?.ShortName ?? "???", team?.CrestUrl,
            standing.Played, standing.Won, standing.Drawn, standing.Lost,
            standing.GoalsFor, standing.GoalsAgainst, standing.GoalDifference, standing.Points);
}
