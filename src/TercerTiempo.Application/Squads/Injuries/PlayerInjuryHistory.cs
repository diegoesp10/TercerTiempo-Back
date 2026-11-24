using System.ComponentModel;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Squads.Injuries;

public sealed record GetPlayerInjuryHistoryQuery(Guid SquadId, Guid SquadPlayerId) : IQuery<PlayerInjuryHistoryDto>;

public sealed record PlayerInjuryHistoryDto(
    Guid SquadPlayerId,
    [property: Description("Sus lesiones en el club, de esta temporada y de las anteriores: la abierta primero y después de la más reciente a la más antigua.")]
    IReadOnlyList<InjuryHistoryEntryDto> Injuries,
    InjuryHistoryTotalsDto Totals);

public sealed record InjuryHistoryEntryDto(
    InjuryDto Injury,
    Guid SquadId,
    string TeamName,
    string SeasonLabel,
    [property: Description("Es de otra plantilla del club (otra temporada u otro equipo): aquí se consulta; se corrige desde aquella.")]
    bool OtherSquad,
    [property: Description("Si pasó abierta desde otra temporada, la del parte: es la misma lesión (sale una sola vez, con las pruebas de las dos); null si empezó en esta.")]
    string? FromSeasonLabel = null);

public sealed record InjuryHistoryTotalsDto(
    int Injuries,
    [property: Description("Lesiones superadas: las que tienen el alta.")]
    int Recovered,
    [property: Description("Días de baja de todas (la abierta, hasta hoy).")]
    int DaysOut,
    [property: Description("Media de días del parte al alta de las superadas; null si no hay ninguna.")]
    double? AverageRecoveryDays,
    [property: Description("Cuántas de ellas fueron recaídas de una anterior.")]
    int Relapses);

internal static class InjuryHistory
{
    // La misma persona en otras plantillas del club: si tiene cuenta, la ficha con esa cuenta (en cualquier equipo del
    // club); si no, la de su mismo nombre en el mismo equipo, y solo si no hay dudas (una única ficha que encaje). Mejor
    // que falte una lesión antigua que enseñar la de otro.
    public static IReadOnlyList<(Squad Squad, SquadPlayer Player)> SamePersonIn(Squad squad, SquadPlayer player, IEnumerable<Squad> others)
    {
        var found = new List<(Squad, SquadPlayer)>();
        foreach (var other in others.Where(o => o.Id != squad.Id))
        {
            if (player.PlayerId is { } account && other.Players.FirstOrDefault(p => p.PlayerId == account) is { } linked)
            {
                found.Add((other, linked));
                continue;
            }

            if (other.TeamId != squad.TeamId) continue;
            var byName = other.Players
                .Where(p => (p.PlayerId is null || player.PlayerId is null) && PersonName.SamePerson(player.NormalizedName, p.NormalizedName))
                .Take(2)
                .ToList();
            if (byName.Count == 1) found.Add((other, byName[0]));
        }

        return found;
    }

    // Las de temporadas anteriores que continúa, de la más reciente a la del parte.
    public static IReadOnlyList<PlayerInjury> Earlier(PlayerInjury injury, IReadOnlyDictionary<Guid, PlayerInjury> byId)
    {
        var chain = new List<PlayerInjury>();
        var current = injury;
        while (current.ContinuesInjuryId is { } previous && byId.TryGetValue(previous, out var found) && found != injury && !chain.Contains(found))
        {
            chain.Add(found);
            current = found;
        }

        return chain;
    }

    public static InjuryHistoryTotalsDto Totals(IReadOnlyCollection<InjuryDto> injuries)
    {
        var recovered = injuries.Where(i => i.ReturnedOn is not null).ToList();
        return new InjuryHistoryTotalsDto(
            injuries.Count,
            recovered.Count,
            injuries.Sum(i => i.DaysOut),
            recovered.Count == 0 ? null : Math.Round(recovered.Average(i => i.DaysOut), 1, MidpointRounding.AwayFromZero),
            injuries.Count(i => i.RelapseOfInjuryId is not null));
    }
}

// El historial de lesiones de un jugador: las de su ficha y las de la misma persona en las plantillas de otras temporadas
// (y otros equipos) del club. Son datos de salud: no salen del club, y un jugador solo ve las suyas.
internal sealed class GetPlayerInjuryHistoryHandler(
    ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, IInjuryStore injuries, IInjuryDocumentStore documents,
    ITeamStore teams, ICompetitionStore competitions, TimeProvider clock)
    : IQueryHandler<GetPlayerInjuryHistoryQuery, PlayerInjuryHistoryDto>
{
    public async Task<PlayerInjuryHistoryDto> Handle(GetPlayerInjuryHistoryQuery query, CancellationToken cancellationToken)
    {
        var squad = await InjuryAccess.SquadForViewerAsync(squads, clubs, currentUser, query.SquadId, clock, cancellationToken);
        var player = squad.Players.FirstOrDefault(p => p.Id == query.SquadPlayerId)
            ?? throw new NotFoundException(ErrorCode.SquadPlayerNotFound, query.SquadPlayerId);
        var visible = await InjuryAccess.VisibleSquadPlayersAsync(squad, currentUser, clock, clubs, cancellationToken);
        if (!visible(player.Id))
            throw new ForbiddenException(ErrorCode.InjuryPrivate);

        var club = await clubs.GetByTeamAsync(squad.TeamId, cancellationToken);
        var teamIds = club is null ? [squad.TeamId] : club.Teams.Select(t => t.TeamId).Append(squad.TeamId).Distinct().ToList();
        var others = await squads.GetByTeamIdsAsync(teamIds, cancellationToken);
        var entries = new List<(Squad Squad, SquadPlayer Player)> { (squad, player) };
        entries.AddRange(InjuryHistory.SamePersonIn(squad, player, others));

        var list = await injuries.GetBySquadPlayersAsync(entries.Select(e => e.Player.Id).ToList(), cancellationToken);
        var bySquad = entries.ToDictionary(e => e.Squad.Id);
        var summaries = (await SquadMapper.ToSummariesAsync(entries.Select(e => e.Squad).ToList(), null, clock, teams, competitions, cancellationToken))
            .ToDictionary(s => s.Id);

        // Las pruebas e informes de otra temporada solo salen a quien las podría abrir allí (el club y el propio jugador;
        // un técnico nuevo, no). El administrador ve que existen, como en la plantilla.
        var opens = new Dictionary<Guid, bool>();
        foreach (var (other, _) in entries)
            opens[other.Id] = other.Id == squad.Id || currentUser.IsAdmin
                || await SquadAccess.IsManagerAsync(other, currentUser, clock, clubs, cancellationToken)
                || (currentUser.PlayerId is { } me && bySquad[other.Id].Player.PlayerId == me);
        var withDocuments = list.Where(i => opens.GetValueOrDefault(i.SquadId)).Select(i => i.Id).ToList();
        var attached = withDocuments.Count == 0 ? [] : await documents.GetByInjuryIdsAsync(withDocuments, cancellationToken);
        var documentsOf = attached.ToLookup(d => d.InjuryId);

        // La que pasó abierta a la temporada siguiente y su continuación son la misma lesión: sale una vez, la de la temporada
        // en la que se lleva, con las pruebas de las dos y la temporada del parte. Así no cuenta dos veces en los totales.
        var known = list.Where(i => bySquad.ContainsKey(i.SquadId) && summaries.ContainsKey(i.SquadId)).ToList();
        var byId = known.ToDictionary(i => i.Id);
        var continued = known.Where(i => i.ContinuesInjuryId is { } previous && byId.ContainsKey(previous)).Select(i => i.ContinuesInjuryId!.Value).ToHashSet();

        var today = SquadAccess.Today(clock);
        var items = known
            .Where(i => !continued.Contains(i.Id))
            .OrderByDescending(i => i.IsActive)
            .ThenByDescending(i => i.StartedOn)
            .Select(i =>
            {
                var earlier = InjuryHistory.Earlier(i, byId);
                var first = earlier.LastOrDefault();
                var (owner, entry) = bySquad[i.SquadId];
                var summary = summaries[i.SquadId];
                var dto = InjuryMapper.ToDto(i, entry, today, earlier.Prepend(i).SelectMany(x => documentsOf[x.Id]));
                // Si aquella era una recaída, esta también.
                if (first is not null) dto = dto with { RelapseOfInjuryId = dto.RelapseOfInjuryId ?? first.RelapseOfInjuryId };
                return new InjuryHistoryEntryDto(dto, owner.Id, summary.TeamName, summary.SeasonLabel, owner.Id != squad.Id,
                    first is null ? null : summaries[first.SquadId].SeasonLabel);
            })
            .ToList();

        return new PlayerInjuryHistoryDto(player.Id, items, InjuryHistory.Totals(items.Select(x => x.Injury).ToList()));
    }
}
