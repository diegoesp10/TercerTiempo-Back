using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Squads;

public sealed record SquadDuplicate(SquadPlayer Keep, IReadOnlyList<SquadPlayer> Duplicates);

// Fichas de plantilla que son la misma persona: las que vienen de fichas de la federación que se juntan en una persona
// (la web publica a algunos dos veces, «Lucas Paredes» y «Lucas Paredes Rivera»). Se queda la de su ficha principal; si
// ninguna lo es, la que tiene cuenta y luego la más antigua.
public static class SquadDuplicates
{
    public static IReadOnlyList<SquadDuplicate> Find(Squad squad, IEnumerable<RosterPerson> people)
    {
        var found = new List<SquadDuplicate>();
        foreach (var person in people)
        {
            var entries = person.Entries.Select(e => e.Id).ToHashSet();
            var players = squad.Players
                .Where(p => p.IsActive && p.RosterEntryId is { } id && entries.Contains(id))
                .OrderByDescending(p => p.RosterEntryId == person.Main.Id)
                .ThenByDescending(p => p.PlayerId is not null)
                .ThenBy(p => p.AddedAt)
                .ThenBy(p => p.Id)
                .ToList();
            if (players.Count > 1) found.Add(new SquadDuplicate(players[0], players.Skip(1).ToList()));
        }

        return found;
    }

    // Filtro barato antes de cargar la plantilla y sus actas: alguna pareja de jugadores con nombres de la misma persona.
    public static bool MayHaveDuplicates(IReadOnlyList<string> normalizedNames)
    {
        for (var i = 0; i < normalizedNames.Count; i++)
            for (var j = i + 1; j < normalizedNames.Count; j++)
                if (PersonName.SamePerson(normalizedNames[i], normalizedNames[j])) return true;
        return false;
    }
}
