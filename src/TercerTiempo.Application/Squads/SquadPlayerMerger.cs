using TercerTiempo.Application.Performances;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Trainings;

namespace TercerTiempo.Application.Squads;

// Junta dos fichas de la misma persona en una plantilla: todo lo que colgaba de la repetida pasa a la que se queda y la
// repetida desaparece. Se guarda por pasos porque las tablas apuntan a la ficha sin cascada y con índices únicos por
// jugador: primero se quita lo que sobra, luego se mueve lo demás, después se borra la ficha y por último la cuenta.
// Si algo falla a medias, volver a juntarlas termina el trabajo.
public sealed class SquadPlayerMerger(
    IPerformanceStore performances,
    IPerformanceSuggestionStore suggestions,
    IInjuryStore injuries,
    IInjuryProposalStore injuryProposals,
    ICallUpStore callUps,
    ITrainingStore trainings,
    ITrialPlayerStore trialPlayers,
    IMatchStore matches,
    IUnitOfWork unitOfWork,
    TimeProvider time)
{
    public async Task MergeAsync(Squad squad, Guid keepId, Guid duplicateId, CancellationToken cancellationToken)
    {
        var keep = squad.Players.Single(p => p.Id == keepId);
        if (!squad.Players.Any(p => p.Id == duplicateId))
            throw new ArgumentException("La ficha repetida no es de esta plantilla.", nameof(duplicateId));
        var now = time.GetUtcNow();

        var moving = await MovablePerformancesAsync(keepId, duplicateId, cancellationToken);
        // Una posible lesión por jugador y partido: si las dos fichas tienen la del mismo partido, se queda la de la otra.
        var proposals = await injuryProposals.GetBySquadAsync(squad.Id, cancellationToken);
        var keptMatches = proposals.Where(p => p.SquadPlayerId == keepId).Select(p => p.MatchId).ToHashSet();
        var movingProposals = new List<InjuryProposal>();
        foreach (var proposal in proposals.Where(p => p.SquadPlayerId == duplicateId))
        {
            if (keptMatches.Contains(proposal.MatchId)) injuryProposals.Remove(proposal);
            else movingProposals.Add(proposal);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var matchMap = (await matches.GetByIdsAsync(moving.Select(l => l.MatchId), cancellationToken)).ToDictionary(m => m.Id);
        foreach (var line in moving)
        {
            if (matchMap.TryGetValue(line.MatchId, out var match)) line.MoveToSquadPlayer(keep, match);
            else performances.Remove(line);
        }

        foreach (var suggestion in (await suggestions.GetBySquadAsync(squad.Id, cancellationToken)).Where(s => s.SquadPlayerId == duplicateId))
            suggestion.MoveToSquadPlayer(keepId);
        foreach (var injury in (await injuries.GetBySquadAsync(squad.Id, cancellationToken)).Where(i => i.SquadPlayerId == duplicateId))
            injury.MoveToSquadPlayer(keepId);
        foreach (var proposal in movingProposals)
            proposal.MoveToSquadPlayer(keepId);
        foreach (var trial in (await trialPlayers.GetBySquadAsync(squad.Id, cancellationToken)).Where(t => t.PromotedSquadPlayerId == duplicateId))
            trial.MovePromotionTo(keepId);
        foreach (var callUp in await callUps.GetBySquadAsync(squad.Id, cancellationToken))
            callUp.MergeEntries(duplicateId, keepId);
        foreach (var session in await trainings.GetBySquadPlayerAsync(duplicateId, cancellationToken))
            session.MergeAttendance(duplicateId, keepId, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var account = squad.MergePlayer(keepId, duplicateId);
        await RatingRecalculator.RecalculateAsync(await performances.GetBySquadPlayerAsync(keepId, cancellationToken), [squad], matches, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (account is { } playerId && keep.PlayerId is null)
        {
            squad.LinkPlayer(keepId, playerId);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    // Las líneas del acta interna de la repetida que pasan a la otra. Si las dos tienen línea del mismo partido se queda
    // la que más minutos apunta (o la última tocada) y la otra se quita.
    private async Task<List<PlayerMatchPerformance>> MovablePerformancesAsync(Guid keepId, Guid duplicateId, CancellationToken cancellationToken)
    {
        var kept = (await performances.GetBySquadPlayerAsync(keepId, cancellationToken)).ToDictionary(l => l.MatchId);
        var moving = new List<PlayerMatchPerformance>();
        foreach (var line in await performances.GetBySquadPlayerAsync(duplicateId, cancellationToken))
        {
            if (!kept.TryGetValue(line.MatchId, out var other))
            {
                moving.Add(line);
            }
            else if ((line.MinutesPlayed, line.UpdatedAt).CompareTo((other.MinutesPlayed, other.UpdatedAt)) > 0)
            {
                performances.Remove(other);
                moving.Add(line);
            }
            else
            {
                performances.Remove(line);
            }
        }

        return moving;
    }
}
