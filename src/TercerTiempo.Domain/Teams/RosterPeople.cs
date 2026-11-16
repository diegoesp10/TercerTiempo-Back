using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Players;

namespace TercerTiempo.Domain.Teams;

// Una persona de la plantilla publicada: su ficha principal y, si la web la publica más de una vez ("Lucas Paredes" y
// "Lucas Paredes Rivera", cada una con su identificador), las demás.
public sealed record RosterPerson(RosterEntry Main, IReadOnlyList<RosterEntry> Entries)
{
    public Position? Position => Main.Position ?? Entries.Select(e => e.Position).FirstOrDefault(p => p is not null);

    public RosterEntry? WithPhoto => Entries.FirstOrDefault(e => e.PhotoKey is not null);
}

public static class RosterPeople
{
    // Junta las fichas de la misma persona (PersonName.SamePerson). Lo que publica la web se guarda tal cual y se
    // concilia al leer: así dos fichas que resultan ser de dos jugadores (different: los dos salen en la misma acta)
    // vuelven a separarse solas. Una ficha que encaja con dos que no encajan entre sí ("Mateo García" con "Mateo García
    // Martínez" y "Mateo García Mazo") no se junta con ninguna: no se sabe de quién es.
    public static IReadOnlyList<RosterPerson> Group(IEnumerable<RosterEntry> entries, Func<RosterEntry, RosterEntry, bool>? different = null)
    {
        var all = entries.ToList();
        var same = all.ToDictionary(e => e.Id, _ => new List<RosterEntry>());
        for (var i = 0; i < all.Count; i++)
        {
            for (var j = i + 1; j < all.Count; j++)
            {
                if (!PersonName.SamePerson(all[i].NormalizedName, all[j].NormalizedName) || different?.Invoke(all[i], all[j]) == true) continue;
                same[all[i].Id].Add(all[j]);
                same[all[j].Id].Add(all[i]);
            }
        }

        bool Linked(RosterEntry a, RosterEntry b) => same[a.Id].Contains(b);
        var unclear = all
            .Where(e => same[e.Id].Any(a => same[e.Id].Any(b => a != b && !Linked(a, b))))
            .Select(e => e.Id)
            .ToHashSet();

        var people = new List<RosterPerson>();
        var placed = new HashSet<Guid>();
        foreach (var entry in all)
        {
            if (!placed.Add(entry.Id)) continue;

            var group = new List<RosterEntry> { entry };
            if (!unclear.Contains(entry.Id))
            {
                foreach (var other in same[entry.Id].Where(o => !unclear.Contains(o.Id) && placed.Add(o.Id)))
                    group.Add(other);
            }

            // Manda la ficha que ha tocado el club y, si no, la del nombre más completo.
            var ordered = group
                .OrderByDescending(e => e.ManagedByClub)
                .ThenByDescending(e => PersonName.Words(e.NormalizedName).Length)
                .ThenByDescending(e => e.Position is not null)
                .ThenByDescending(e => e.PhotoKey is not null)
                .ThenBy(e => e.NormalizedName, StringComparer.Ordinal)
                .ThenBy(e => e.Id)
                .ToList();
            people.Add(new RosterPerson(ordered[0], ordered));
        }

        return people;
    }
}
