using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Performances;

internal static class RatingRecalculator
{
    public static async Task<int> RecalculateAsync(
        IReadOnlyCollection<PlayerMatchPerformance> lines,
        IReadOnlyCollection<Squad> knownSquads,
        IMatchStore matches,
        CancellationToken cancellationToken,
        ISquadStore? squads = null)
    {
        if (lines.Count == 0) return 0;

        // Las plantillas y los partidos que faltan, de una vez (antes, una consulta por línea nueva).
        var squadMap = knownSquads.ToDictionary(s => s.Id);
        var missingSquads = lines.Select(l => l.SquadId).Where(id => !squadMap.ContainsKey(id)).Distinct().ToList();
        if (squads is not null && missingSquads.Count > 0)
            foreach (var loaded in await squads.GetByIdsAsync(missingSquads, cancellationToken))
                squadMap[loaded.Id] = loaded;
        var matchMap = (await matches.GetByIdsAsync(lines.Select(l => l.MatchId), cancellationToken)).ToDictionary(m => m.Id);
        var recalculated = 0;

        foreach (var line in lines)
        {
            if (!squadMap.TryGetValue(line.SquadId, out var squad) || !matchMap.TryGetValue(line.MatchId, out var match)) continue;

            var player = squad.Players.FirstOrDefault(p => p.Id == line.SquadPlayerId);
            if (player is null) continue;

            line.Recalculate(player, match);
            recalculated++;
        }

        return recalculated;
    }
}
