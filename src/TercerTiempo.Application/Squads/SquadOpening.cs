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
        ImportRoster(squad, await teams.GetRosterAsync(teamId, seasonId, cancellationToken), now);

        squads.Add(squad);
        return squad;
    }

    // Las fichas publicadas que aún no están en la plantilla. Un nombre que ya está no se duplica: lo añadió el club a
    // mano o el proveedor publica al mismo jugador dos veces (dos fichas con distinto id y el mismo nombre).
    public static void ImportRoster(Squad squad, IEnumerable<RosterEntry> roster, DateTimeOffset now)
    {
        foreach (var entry in roster)
        {
            if (squad.ActivePlayers.Any(p => p.RosterEntryId == entry.Id || p.NormalizedName == entry.NormalizedName))
                continue;

            squad.AddPlayer(entry.FullName, entry.Position, entry.ShirtNumber, entry.Id, now);
        }
    }
}
