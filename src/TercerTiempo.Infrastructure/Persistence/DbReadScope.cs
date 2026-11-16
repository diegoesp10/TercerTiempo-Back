using Microsoft.EntityFrameworkCore;
using TercerTiempo.Application.Abstractions.Messaging;

namespace TercerTiempo.Infrastructure.Persistence;

// Las consultas (IQueryHandler) leen sin seguimiento; al acabar se vuelve a como estaba (una consulta dentro de un comando
// deja al comando seguir con sus entidades seguidas).
internal sealed class DbReadScope(AppDbContext db) : IReadScope
{
    public IDisposable Begin()
    {
        var previous = db.ChangeTracker.QueryTrackingBehavior;
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        return new Restore(db, previous);
    }

    private sealed class Restore(AppDbContext db, QueryTrackingBehavior previous) : IDisposable
    {
        public void Dispose() => db.ChangeTracker.QueryTrackingBehavior = previous;
    }
}
