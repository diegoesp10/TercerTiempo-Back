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
    public static IReadOnlyList<SquadPlayer> ImportRoster(Squad squad, IEnumerable<RosterEntry> roster, DateTimeOffset now)
    {
        var people = RosterPeople.Group(roster);
        var completed = CompletePositions(squad, people);
        foreach (var person in people)
        {
            var fichas = person.Entries.Select(e => e.Id).ToHashSet();
            if (squad.ActivePlayers.Any(p => p.RosterEntryId is { } id && fichas.Contains(id) || PersonName.SamePerson(p.NormalizedName, person.Main.NormalizedName)))
                continue;

            squad.AddPlayer(person.Main.FullName, person.Position, null, person.Main.Id, now);
        }

        return completed;
    }

    // Quien entró sin posición (la federación no la publicaba) toma la que publica ahora para esa persona, en su ficha o en
    // otra suya ("Lucas Paredes" y "Lucas Paredes Rivera"). Al que se añadió a mano solo se le pone si su nombre encaja con
    // una sola persona. Nunca se pisa la que ha puesto el club. Devuelve a los que se les ha puesto.
    public static IReadOnlyList<SquadPlayer> CompletePositions(Squad squad, IReadOnlyList<RosterPerson> people)
    {
        var completed = new List<SquadPlayer>();
        foreach (var player in squad.ActivePlayers.Where(p => p.Position is null).ToList())
        {
            var person = player.RosterEntryId is { } entryId
                ? people.FirstOrDefault(p => p.Entries.Any(e => e.Id == entryId))
                : OnlyOne(people.Where(p => PersonName.SamePerson(p.Main.NormalizedName, player.NormalizedName)));

            if (person?.Position is { } position && squad.CompletePlayerPosition(player.Id, position))
                completed.Add(player);
        }

        return completed;
    }

    private static RosterPerson? OnlyOne(IEnumerable<RosterPerson> candidates)
    {
        var two = candidates.Take(2).ToList();
        return two.Count == 1 ? two[0] : null;
    }
}
