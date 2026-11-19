using TercerTiempo.Application.Performances;
using TercerTiempo.Application.Teams.Roster;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Squads;

public sealed record SquadReconciliationResult(int MergedPlayers, int CompletedPositions, int OfficialRatings = 0);

// Tras cada copia de los datos reales, las plantillas se ponen al día con lo que publica la federación: las fichas
// repetidas de una misma persona se juntan, a quien entró sin posición se le pone la que publique después y las notas
// toman los goles que se encajaron con cada uno en el campo según el acta oficial cerrada. El resto de posiciones las
// elige el club: es obligatoria al dar de alta, editar o hacer ficha.
public sealed class SquadRosterReconciliation(
    ISquadStore squads,
    ITeamStore teams,
    IPerformanceStore performances,
    IMatchStore matches,
    IOfficialSheetStore sheets,
    SquadPlayerMerger merger,
    OfficialSheetRatings officialRatings,
    IUnitOfWork unitOfWork)
{
    public async Task<SquadReconciliationResult> ReconcileAsync(CancellationToken cancellationToken)
    {
        var merged = await MergeDuplicatesAsync(cancellationToken);
        var completed = await CompletePositionsAsync(cancellationToken);
        var ratings = await officialRatings.SyncAllAsync(cancellationToken);
        if (ratings > 0) await unitOfWork.SaveChangesAsync(cancellationToken);
        return new SquadReconciliationResult(merged, completed, ratings);
    }

    private async Task<int> MergeDuplicatesAsync(CancellationToken cancellationToken)
    {
        var candidates = (await squads.GetActivePlayerNamesAsync(cancellationToken))
            .GroupBy(p => p.SquadId)
            .Where(g => SquadDuplicates.MayHaveDuplicates(g.Select(p => p.NormalizedName).ToList()))
            .Select(g => g.Key)
            .ToList();

        var merged = 0;
        foreach (var squadId in candidates)
        {
            var squad = await squads.GetByIdAsync(squadId, cancellationToken);
            if (squad is null) continue;

            // Dos fichas que salen en la misma acta oficial son dos jugadores aunque se llamen igual: no se juntan.
            var roster = await teams.GetRosterAsync(squad.TeamId, squad.SeasonId, cancellationToken);
            var people = await RosterSummary.PeopleAsync(squad.TeamId, squad.SeasonId, roster, matches, sheets, cancellationToken);
            foreach (var duplicate in SquadDuplicates.Find(squad, people.Select(p => p.Person)))
                foreach (var other in duplicate.Duplicates)
                {
                    await merger.MergeAsync(squad, duplicate.Keep.Id, other.Id, cancellationToken);
                    merged++;
                }
        }

        return merged;
    }

    private async Task<int> CompletePositionsAsync(CancellationToken cancellationToken)
    {
        var completed = 0;
        foreach (var squad in await squads.GetWithPlayersMissingPositionAsync(cancellationToken))
        {
            var people = RosterPeople.Group(await teams.GetRosterAsync(squad.TeamId, squad.SeasonId, cancellationToken));
            var players = SquadOpening.CompletePositions(squad, people);
            await RecalculateRatingsAsync(squad, players, performances, matches, cancellationToken);
            completed += players.Count;
        }

        if (completed > 0) await unitOfWork.SaveChangesAsync(cancellationToken);
        return completed;
    }

    // La nota depende de la posición: las actas de quien acaba de tenerla se recalculan.
    internal static async Task RecalculateRatingsAsync(
        Squad squad, IReadOnlyList<SquadPlayer> players, IPerformanceStore performances, IMatchStore matches, CancellationToken cancellationToken)
    {
        foreach (var player in players)
            await RatingRecalculator.RecalculateAsync(await performances.GetBySquadPlayerAsync(player.Id, cancellationToken), [squad], matches, cancellationToken);
    }
}
