using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Squads;

internal static class SquadOpening
{
    // La plantilla de un equipo en una temporada: la que ya existe o una nueva con las fichas publicadas del equipo.
    public static async Task<Squad> OpenAsync(
        ISquadStore squads, ITeamStore teams, Guid teamId, Guid seasonId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await squads.GetByTeamAndSeasonAsync(teamId, seasonId, cancellationToken) is { } existing)
            return existing;

        var squad = Squad.Open(teamId, seasonId, now);
        foreach (var entry in await teams.GetRosterAsync(teamId, seasonId, cancellationToken))
            squad.AddPlayer(entry.FullName, entry.Position, entry.ShirtNumber, entry.Id, now);

        squads.Add(squad);
        return squad;
    }
}
