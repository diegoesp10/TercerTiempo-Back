using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Performances;

/// <summary>Recalcula la nota de líneas del acta cargando las plantillas (posiciones) y partidos (resultados) que hagan falta.</summary>
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

        var squadMap = knownSquads.ToDictionary(s => s.Id);
        var matchMap = new Dictionary<Guid, Match>();
        var recalculated = 0;

        foreach (var line in lines)
        {
            if (!squadMap.TryGetValue(line.SquadId, out var squad))
            {
                if (squads is null || await squads.GetByIdAsync(line.SquadId, cancellationToken) is not { } loaded) continue;
                squadMap[loaded.Id] = squad = loaded;
            }

            if (!matchMap.TryGetValue(line.MatchId, out var match))
            {
                if (await matches.GetByIdAsync(line.MatchId, cancellationToken) is not { } loaded) continue;
                matchMap[loaded.Id] = match = loaded;
            }

            var player = squad.Players.FirstOrDefault(p => p.Id == line.SquadPlayerId);
            if (player is null) continue;

            line.Recalculate(player, match);
            recalculated++;
        }

        return recalculated;
    }
}
