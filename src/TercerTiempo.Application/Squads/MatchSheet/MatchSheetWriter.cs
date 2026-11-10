using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Squads.MatchSheet;

// El único camino por el que se escribe el acta interna: lo usan el PUT del acta, el de los datos de equipo y el
// volcado de la propuesta del vídeo. No guarda: quien lo llama cierra la unidad de trabajo.
internal sealed class MatchSheetWriter(IPerformanceStore performances, ITeamMatchStatsStore teamStats)
{
    public async Task WriteLinesAsync(
        Squad squad, Match match, SquadEditor editor, IReadOnlyList<MatchSheetEntryInput> entries, DateTimeOffset now, CancellationToken cancellationToken)
    {
        PerformancePolicy.EnsureCanRecord(squad, editor, match, Squad.Today(now));

        var existing = (await performances.GetBySquadAndMatchAsync(squad.Id, match.Id, cancellationToken))
            .ToDictionary(p => p.SquadPlayerId);

        foreach (var input in entries)
        {
            var player = squad.GetPlayer(input.SquadPlayerId);
            existing.TryGetValue(player.Id, out var current);

            if (!input.Played)
            {
                if (current is not null)
                    performances.Remove(current);
                continue;
            }

            var stats = input.ToStats();

            if (current is null)
            {
                current = PlayerMatchPerformance.Record(squad, player, match, editor, stats, now);
                performances.Add(current);
            }
            else
            {
                current.Update(stats, player, match, editor, now);
            }

            current.SetShirtNumber(input.ShirtNumber);
        }
    }

    public async Task<TeamMatchStats> WriteTeamStatsAsync(
        Squad squad, Match match, SquadEditor editor, TeamMatchStatsData data, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var current = await teamStats.GetAsync(squad.Id, match.Id, cancellationToken);
        if (current is null)
        {
            current = TeamMatchStats.Record(squad, match, editor, data, now);
            teamStats.Add(current);
        }
        else
        {
            current.Update(squad, match, editor, data, now);
        }

        return current;
    }
}
