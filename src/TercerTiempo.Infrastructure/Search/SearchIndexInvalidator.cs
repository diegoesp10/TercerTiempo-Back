using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TercerTiempo.Application.Search;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Infrastructure.Search;

// Al guardar cambios en lo que enlaza el buscador (plantillas y sus jugadores, fichas, equipos, clubes, ligas, cuentas
// de jugador), el índice se da por viejo cuando el guardado ha terminado: la siguiente búsqueda ya ve lo nuevo.
internal sealed class SearchIndexInvalidator(SearchIndexHolder holder) : SaveChangesInterceptor
{
    private static readonly Type[] Indexed =
        [typeof(Squad), typeof(SquadPlayer), typeof(RosterEntry), typeof(Team), typeof(Club), typeof(ClubTeam), typeof(Competition), typeof(Season), typeof(Player)];

    private readonly ConditionalWeakTable<DbContext, object> _pending = [];

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
        var touched = context.ChangeTracker.Entries()
            .Any(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && Indexed.Contains(e.Metadata.ClrType));
        if (touched) _pending.AddOrUpdate(context, true);
    }

    private void Flush(DbContext? context)
    {
        if (context is not null && _pending.Remove(context)) holder.Invalidate();
    }
}
