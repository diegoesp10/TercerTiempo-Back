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
// de jugador, técnicos y sus nombramientos), el índice se da por viejo cuando el guardado ha terminado y se rehace en
// segundo plano. Las lecturas públicas en memoria (PublicDataCache) solo pierden lo que depende de lo guardado: ligas,
// partidos y clasificaciones de esa temporada, o árbitros.
internal sealed class SearchIndexInvalidator(SearchIndexHolder holder, PublicDataCache publicData) : SaveChangesInterceptor
{
    private static readonly Type[] Indexed =
        [typeof(Squad), typeof(SquadPlayer), typeof(RosterEntry), typeof(Team), typeof(Club), typeof(ClubTeam), typeof(Competition), typeof(Season), typeof(Player),
         typeof(Coach), typeof(CoachAppointment)];

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
        var search = false;
        var tags = new HashSet<string>();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            search |= Indexed.Contains(entry.Metadata.ClrType);
            tags.UnionWith(PublicTagsOf(entry.Entity));
        }
        if (search || tags.Count > 0) _pending.AddOrUpdate(context, new Pending(search, tags));
    }

    internal static IEnumerable<string> PublicTagsOf(object entity) => entity switch
    {
        Match match => [PublicTags.Matches(match.SeasonId), PublicTags.AllMatches],
        Standing standing => [PublicTags.Standings(standing.SeasonId), PublicTags.AllStandings],
        Referee or MatchOfficial or RefereeRating => [PublicTags.Referees],
        SeasonYear or Competition or Season or SeasonZone or Team => [PublicTags.Structure],
        _ => [],
    };

    private void Flush(DbContext? context)
    {
        if (context is null || !_pending.TryGetValue(context, out var pending)) return;
        _pending.Remove(context);
        if (pending.Search) holder.Invalidate();
        if (pending.PublicTags.Count > 0) publicData.Invalidate(pending.PublicTags);
    }

    private sealed record Pending(bool Search, IReadOnlySet<string> PublicTags);
}
