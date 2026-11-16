using TercerTiempo.Domain.Common;
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

    // Las personas publicadas que aún no están en la plantilla, una vez cada una: el proveedor publica a algunos
    // jugadores dos veces ("Lucas Paredes" y "Lucas Paredes Rivera", con distinto id) y el club puede haberlos añadido
    // ya a mano. Llegan sin dorsal (la ficha publicada no lo lleva): el habitual lo pone el cuerpo técnico y el de cada
    // partido va en su acta.
    public static void ImportRoster(Squad squad, IEnumerable<RosterEntry> roster, DateTimeOffset now)
    {
        foreach (var person in RosterPeople.Group(roster))
        {
            var fichas = person.Entries.Select(e => e.Id).ToHashSet();
            if (squad.ActivePlayers.Any(p => p.RosterEntryId is { } id && fichas.Contains(id) || PersonName.SamePerson(p.NormalizedName, person.Main.NormalizedName)))
                continue;

            squad.AddPlayer(person.Main.FullName, person.Position, null, person.Main.Id, now);
        }
    }
}
