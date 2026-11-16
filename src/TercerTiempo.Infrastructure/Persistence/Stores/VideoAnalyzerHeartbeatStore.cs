using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

// Fuera de la unidad de trabajo: el latido se escribe solo, cada medio minuto, y nunca arrastra otros cambios.
internal sealed class VideoAnalyzerHeartbeatStore(AppDbContext db) : IVideoAnalyzerHeartbeatStore
{
    // Los analizadores que se pararon sin despedirse (un corte de luz) se olvidan pasado un día.
    private static readonly TimeSpan Forget = TimeSpan.FromDays(1);

    public async Task BeatAsync(string workerId, DateTimeOffset startedAt, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var updated = await db.VideoAnalyzerHeartbeats
            .Where(h => h.WorkerId == workerId)
            .ExecuteUpdateAsync(set => set.SetProperty(h => h.SeenAt, now), cancellationToken);
        if (updated > 0) return;

        var stale = now - Forget;
        await db.VideoAnalyzerHeartbeats.Where(h => h.SeenAt < stale).ExecuteDeleteAsync(cancellationToken);
        db.VideoAnalyzerHeartbeats.Add(new VideoAnalyzerHeartbeat(workerId, startedAt, now));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<DateTimeOffset?> LastSeenAsync(CancellationToken cancellationToken = default) =>
        await db.VideoAnalyzerHeartbeats.AsNoTracking().MaxAsync(h => (DateTimeOffset?)h.SeenAt, cancellationToken);

    public Task RemoveAsync(string workerId, CancellationToken cancellationToken = default) =>
        db.VideoAnalyzerHeartbeats.Where(h => h.WorkerId == workerId).ExecuteDeleteAsync(cancellationToken);
}
