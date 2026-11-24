using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Squads.Injuries;

public sealed record CarryOverInjuriesCommand : ICommand<InjuryCarryOverResult>;

public sealed record InjuryCarryOverResult(int Carried, int Ended)
{
    public static readonly InjuryCarryOverResult None = new(0, 0);

    public bool Changed => Carried + Ended > 0;

    public static InjuryCarryOverResult operator +(InjuryCarryOverResult a, InjuryCarryOverResult b) => new(a.Carried + b.Carried, a.Ended + b.Ended);
}

// Las lesiones que siguen abiertas al cambiar de temporada pasan a la plantilla del club de la temporada siguiente en la
// que esté la misma persona (con cuenta, en cualquier equipo del club; sin ella, por su nombre en el mismo equipo y solo
// si no hay dudas): no sale disponible en la temporada nueva y los días de baja se siguen contando desde el parte. La de
// la temporada anterior queda allí como historial. Solo cuando la temporada nueva ya ha empezado o es la actual: preparar
// pronto la plantilla siguiente no puede vaciar la enfermería de una temporada que aún se juega.
public sealed class InjuryCarryOver(
    ISquadStore squads, IClubStore clubs, IInjuryStore injuries, ICompetitionStore competitions, ISeasonYearStore seasonYears, TimeProvider clock)
{
    // Al abrir una plantilla o al entrarle jugadores. Puede no estar guardada todavía: se usa tal cual llega.
    public async Task<InjuryCarryOverResult> IntoAsync(Squad squad, CancellationToken cancellationToken)
    {
        var years = await seasonYears.GetAllAsync(cancellationToken);
        return await ForClubAsync(await ClubTeamsAsync(squad.TeamId, cancellationToken), squad, years, cancellationToken);
    }

    // Lo que quede por pasar en toda la app (el trabajo periódico): solo se mira el club de las lesiones abiertas de una
    // temporada que ya tiene siguiente.
    public async Task<InjuryCarryOverResult> AllAsync(CancellationToken cancellationToken)
    {
        var open = await injuries.GetOpenAsync(null, cancellationToken);
        if (open.Count == 0) return InjuryCarryOverResult.None;

        var years = await seasonYears.GetAllAsync(cancellationToken);
        var owners = await squads.GetByIdsAsync(open.Select(i => i.SquadId).Distinct().ToList(), cancellationToken);
        var yearOf = await YearsOfAsync(owners, cancellationToken);

        var result = InjuryCarryOverResult.None;
        var seen = new HashSet<Guid>();
        foreach (var owner in owners)
        {
            if (seen.Contains(owner.TeamId) || !yearOf.TryGetValue(owner.SeasonId, out var year) || Next(years, year) is null) continue;

            var teamIds = await ClubTeamsAsync(owner.TeamId, cancellationToken);
            seen.UnionWith(teamIds);
            result += await ForClubAsync(teamIds, null, years, cancellationToken);
        }

        return result;
    }

    private async Task<InjuryCarryOverResult> ForClubAsync(
        IReadOnlyList<Guid> teamIds, Squad? fresh, IReadOnlyList<SeasonYear> years, CancellationToken cancellationToken)
    {
        var clubSquads = (await squads.GetByTeamIdsAsync(teamIds, cancellationToken)).ToList();
        if (fresh is not null)
        {
            clubSquads.RemoveAll(s => s.Id == fresh.Id);
            clubSquads.Add(fresh);
        }

        var open = await injuries.GetOpenAsync(clubSquads.Select(s => s.Id).ToList(), cancellationToken);
        if (open.Count == 0) return InjuryCarryOverResult.None;

        var yearOf = await YearsOfAsync(clubSquads, cancellationToken);
        var today = SquadAccess.Today(clock);
        var picks = new List<(PlayerInjury Injury, Squad Target, SquadPlayer Player)>();
        foreach (var injury in open)
        {
            var old = clubSquads.FirstOrDefault(s => s.Id == injury.SquadId);
            // De quien causó baja con la lesión abierta, el club solo sabe hasta ese día: no se lleva a ningún sitio.
            if (old?.Players.FirstOrDefault(p => p.Id == injury.SquadPlayerId) is not { IsActive: true } player) continue;
            if (!yearOf.TryGetValue(old.SeasonId, out var year) || Next(years, year) is not { } next) continue;
            if (!next.IsCurrent && next.StartsOn > today) continue;

            var targets = clubSquads.Where(s => yearOf.TryGetValue(s.SeasonId, out var y) && y == next.Id).ToList();
            if (Pick(old, player, targets) is { } pick) picks.Add((injury, pick.Squad, pick.Player));
        }

        if (picks.Count == 0) return InjuryCarryOverResult.None;

        var there = (await injuries.GetBySquadPlayersAsync(picks.Select(p => p.Player.Id).Distinct().ToList(), cancellationToken))
            .GroupBy(i => i.SquadPlayerId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var now = clock.GetUtcNow();
        int carried = 0, ended = 0;
        foreach (var (injury, target, player) in picks)
        {
            // De baja en la temporada nueva: se pasa si vuelve (Reincorporar).
            if (!player.IsActive) continue;

            if (!there.TryGetValue(player.Id, out var theirs)) there[player.Id] = theirs = [];
            // Allí ya llevan una abierta o alguna desde aquel parte (la registraron a mano): no se duplica.
            if (theirs.Any(i => i.IsActive || i.StartedOn >= injury.StartedOn))
            {
                injury.PassOn(now);
                ended++;
                continue;
            }

            var continuation = injury.ContinueIn(target, player, now);
            injuries.Add(continuation);
            theirs.Add(continuation);
            carried++;
        }

        return new InjuryCarryOverResult(carried, ended);
    }

    // Con cuenta manda la cuenta; si la persona encaja en varias plantillas, la de su mismo equipo. Con dudas, ninguna:
    // mejor que falte una lesión que ponérsela a otro.
    internal static (Squad Squad, SquadPlayer Player)? Pick(Squad old, SquadPlayer player, IReadOnlyList<Squad> targets)
    {
        var found = InjuryHistory.SamePersonIn(old, player, targets);
        var byAccount = found.Where(f => player.PlayerId is { } account && f.Player.PlayerId == account).ToList();
        var pool = byAccount.Count > 0 ? byAccount : found.ToList();
        if (pool.Count == 1) return pool[0];

        var sameTeam = pool.Where(f => f.Squad.TeamId == old.TeamId).ToList();
        return sameTeam.Count == 1 ? sameTeam[0] : null;
    }

    private static SeasonYear? Next(IReadOnlyList<SeasonYear> years, Guid yearId) =>
        years.FirstOrDefault(y => y.Id == yearId) is { } year ? years.Where(y => y.StartsOn > year.StartsOn).MinBy(y => y.StartsOn) : null;

    // De qué temporada de la app es cada plantilla (su temporada es la de su liga).
    private async Task<Dictionary<Guid, Guid>> YearsOfAsync(IEnumerable<Squad> list, CancellationToken cancellationToken)
    {
        var map = new Dictionary<Guid, Guid>();
        var seasonIds = list.Select(s => s.SeasonId).Distinct().ToList();
        if (seasonIds.Count == 0) return map;

        foreach (var season in (await competitions.GetBySeasonIdsAsync(seasonIds, cancellationToken)).SelectMany(c => c.Seasons))
            map.TryAdd(season.Id, season.SeasonYearId);
        return map;
    }

    private async Task<IReadOnlyList<Guid>> ClubTeamsAsync(Guid teamId, CancellationToken cancellationToken)
    {
        var club = await clubs.GetByTeamAsync(teamId, cancellationToken);
        return club is null ? [teamId] : club.Teams.Select(t => t.TeamId).Append(teamId).Distinct().ToList();
    }
}

internal sealed class CarryOverInjuriesHandler(InjuryCarryOver carryOver, IUnitOfWork unitOfWork)
    : ICommandHandler<CarryOverInjuriesCommand, InjuryCarryOverResult>
{
    public async Task<InjuryCarryOverResult> Handle(CarryOverInjuriesCommand command, CancellationToken cancellationToken)
    {
        var result = await carryOver.AllAsync(cancellationToken);
        if (result.Changed) await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }
}
