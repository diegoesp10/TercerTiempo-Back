using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Search;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Referees;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Standings;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Infrastructure.Search;

// Al guardar cambios en lo que enlaza el buscador (plantillas y sus jugadores, fichas, equipos, clubes, ligas, cuentas
// de jugador, técnicos y sus nombramientos), el índice se da por viejo cuando el guardado ha terminado: la siguiente búsqueda ya ve lo nuevo. Lo mismo
// con las lecturas públicas en memoria (PublicDataCache) cuando cambian ligas, partidos, clasificaciones o árbitros.
internal sealed class SearchIndexInvalidator(SearchIndexHolder holder, PublicDataCache publicData) : SaveChangesInterceptor
{
    private static readonly Type[] Indexed =
        [typeof(Squad), typeof(SquadPlayer), typeof(RosterEntry), typeof(Team), typeof(Club), typeof(ClubTeam), typeof(Competition), typeof(Season), typeof(Player),
         typeof(Coach), typeof(CoachAppointment)];

    private static readonly Type[] Public =
        [typeof(SeasonYear), typeof(Competition), typeof(Season), typeof(Team), typeof(Match), typeof(Standing), typeof(SeasonZone),
         typeof(Referee), typeof(MatchOfficial), typeof(RefereeRating)];

    private readonly ConditionalWeakTable<DbContext, Pending> _pending = [];

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Track(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Track(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Flush(eventData.Context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Flush(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    private void Track(DbContext? context)
    {
        if (context is null) return;
        var changed = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => e.Metadata.ClrType)
            .Distinct()
            .ToList();
        var pending = new Pending(changed.Any(Indexed.Contains), changed.Any(Public.Contains));
        if (pending.Search || pending.PublicData) _pending.AddOrUpdate(context, pending);
    }

    private void Flush(DbContext? context)
    {
        if (context is null || !_pending.TryGetValue(context, out var pending)) return;
        _pending.Remove(context);
        if (pending.Search) holder.Invalidate();
        if (pending.PublicData) publicData.Invalidate();
    }

    private sealed record Pending(bool Search, bool PublicData);
}
