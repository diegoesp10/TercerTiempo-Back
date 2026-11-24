using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Performances;

// Lleva a las actas internas lo que dice el acta oficial cerrada y no se apunta a mano: los goles que encajó el equipo con
// cada jugador en el campo, que cuentan en su nota (y en su portería a cero). Se hace al escribir el acta interna, tras
// cada copia de los datos reales (el acta oficial puede llegar o cambiar después) y al arrancar.
public sealed class OfficialSheetRatings(ISquadStore squads, IMatchStore matches, IOfficialSheetStore sheets, IPerformanceStore performances)
{
    // Todas las líneas con acta oficial en su partido (o que ya tomaron algo de ella). Devuelve cuántas cambiaron.
    public async Task<int> SyncAllAsync(CancellationToken cancellationToken)
    {
        var lines = await performances.GetWithOfficialSheetDataAsync(cancellationToken);
        if (lines.Count == 0) return 0;

        var squadMap = (await squads.GetByIdsAsync(lines.Select(l => l.SquadId).Distinct().ToList(), cancellationToken)).ToDictionary(s => s.Id);
        var matchIds = lines.Select(l => l.MatchId).Distinct().ToList();
        var matchMap = (await matches.GetByIdsAsync(matchIds, cancellationToken)).ToDictionary(m => m.Id);
        var sheetMap = (await sheets.GetByMatchIdsAsync(matchIds, cancellationToken)).Where(s => s.IsAvailable).ToDictionary(s => s.MatchId);

        var changed = 0;
        foreach (var group in lines.GroupBy(l => (l.SquadId, l.MatchId)))
            if (squadMap.TryGetValue(group.Key.SquadId, out var squad) && matchMap.TryGetValue(group.Key.MatchId, out var match))
                changed += Apply(squad, match, sheetMap.GetValueOrDefault(match.Id), group.ToList());
        return changed;
    }

    // Las líneas de una plantilla en un partido, recién escritas.
    public async Task<int> SyncAsync(Squad squad, Match match, IReadOnlyCollection<PlayerMatchPerformance> lines, CancellationToken cancellationToken)
    {
        if (lines.Count == 0) return 0;
        var sheet = await sheets.GetByMatchAsync(match.Id, cancellationToken);
        return Apply(squad, match, sheet is { IsAvailable: true } ? sheet : null, lines);
    }

    private static int Apply(Squad squad, Match match, OfficialMatchSheet? sheet, IReadOnlyCollection<PlayerMatchPerformance> lines)
    {
        var side = match.IsHome(squad.TeamId) ? MatchSide.Home : MatchSide.Away;
        var withLine = lines.Select(l => l.SquadPlayerId).ToHashSet();
        var official = sheet is not null
            ? SheetMatcher.Match(squad.Players.Where(p => p.WasMemberAt(match.KickoffAt) || withLine.Contains(p.Id)), sheet.LinesOf(side))
            : new Dictionary<Guid, OfficialSheetLine>();

        var changed = 0;
        foreach (var line in lines)
        {
            var player = squad.Players.FirstOrDefault(p => p.Id == line.SquadPlayerId);
            if (player is null) continue;

            var conceded = sheet is not null && official.TryGetValue(player.Id, out var officialLine)
                ? GoalsOnPitch.Conceded(sheet, side, officialLine, match.GoalsAgainst(squad.TeamId))
                : null;
            if (line.SetGoalsConcededOnPitch(conceded, player, match)) changed++;
        }

        return changed;
    }
}

// Los goles que encajó un equipo mientras un jugador estaba en el campo: el minuto de cada gol contra el de su entrada y
// el de su salida (la roja también lo saca). Un cambio en el mismo minuto que un gol se toma como posterior a él: los
// cambios suelen llegar después de un gol, no justo antes. Si los goles del acta no cuadran con el marcador, no se sabe
// (null).
internal static class GoalsOnPitch
{
    public static int? Conceded(OfficialMatchSheet sheet, MatchSide side, OfficialSheetLine line, int? goalsAgainst)
    {
        if (!sheet.IsAvailable || !line.Played || line.Side != side || goalsAgainst is null) return null;

        var goals = sheet.Events.Where(e => Against(e, side)).ToList();
        if (goals.Count != goalsAgainst) return null;

        var from = line.Started ? -1 : line.MinuteIn!.Value;
        return goals.Count(e => e.Minute > from && (line.MinuteOut is not { } minuteOut || e.Minute <= minuteOut));
    }

    // El gol en propia puerta lo apunta el acta del lado de quien lo marca.
    private static bool Against(OfficialSheetEvent e, MatchSide side) => e.Type switch
    {
        OfficialEventType.Goal or OfficialEventType.PenaltyGoal => e.Side != side,
        OfficialEventType.OwnGoal => e.Side == side,
        _ => false,
    };
}
