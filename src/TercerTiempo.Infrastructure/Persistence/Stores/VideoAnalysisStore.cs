using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

internal sealed class MatchVideoAnalysisStore(AppDbContext db) : IMatchVideoAnalysisStore
{
    public Task<MatchVideoAnalysis?> GetByVideoAsync(Guid videoId, CancellationToken cancellationToken = default) =>
        db.MatchVideoAnalyses.FirstOrDefaultAsync(a => a.VideoId == videoId, cancellationToken);

    public async Task<IReadOnlyList<MatchVideoAnalysis>> GetByVideosAsync(IEnumerable<Guid> videoIds, CancellationToken cancellationToken = default)
    {
        var ids = videoIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        return await db.MatchVideoAnalyses.Where(a => ids.Contains(a.VideoId)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<VideoAnalysisStampRow>> GetStampRowsAsync(Guid squadId, Guid matchId, CancellationToken cancellationToken = default)
    {
        var videos = await db.MatchVideos.AsNoTracking()
            .Where(v => v.SquadId == squadId && v.MatchId == matchId && v.DeletedAt == null)
            .Select(v => new { v.Id, v.Status, v.ProcessingProgress })
            .ToListAsync(cancellationToken);
        if (videos.Count == 0) return [];

        var ids = videos.Select(v => v.Id).ToList();
        var analyses = await db.MatchVideoAnalyses.AsNoTracking()
            .Where(a => ids.Contains(a.VideoId))
            .Select(a => new
            {
                a.VideoId, a.Id, a.Status, a.Progress, a.Attempts, a.RequestedAt, a.StartedAt, a.FinishedAt, a.LiveAt, a.PausedAt,
                a.PausedSeconds, a.WorkerId, a.AppliedAt, a.OurTeam, LogLength = a.LogJson == null ? 0 : a.LogJson.Length,
            })
            .ToDictionaryAsync(a => a.VideoId, cancellationToken);

        return videos
            .Select(v => analyses.GetValueOrDefault(v.Id) is { } a
                ? new VideoAnalysisStampRow(
                    v.Id, v.Status, v.ProcessingProgress, a.Id, a.Status, a.Progress, a.Attempts, a.RequestedAt, a.StartedAt, a.FinishedAt,
                    a.LiveAt, a.PausedAt, a.PausedSeconds,
                    a.Status == VideoAnalysisStatus.Running || (a.Status == VideoAnalysisStatus.Paused && a.WorkerId != null),
                    a.AppliedAt, a.OurTeam, a.LogLength)
                : new VideoAnalysisStampRow(v.Id, v.Status, v.ProcessingProgress, null, null, 0, 0, null, null, null, null, null, 0, false, null, null, 0))
            .ToList();
    }

    public void Add(MatchVideoAnalysis analysis) => db.MatchVideoAnalyses.Add(analysis);

    public void Remove(MatchVideoAnalysis analysis) => db.MatchVideoAnalyses.Remove(analysis);

    public Task<MatchVideoAnalysis?> GetNextPendingAsync(int maxAttempts, CancellationToken cancellationToken = default) =>
        db.MatchVideoAnalyses
            .Where(a => a.Status == VideoAnalysisStatus.Pending && a.Attempts < maxAttempts
                && db.MatchVideos.Any(v => v.Id == a.VideoId && v.DeletedAt == null))
            .OrderBy(a => a.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<MatchVideoAnalysis>> GetRunningAsync(CancellationToken cancellationToken = default) =>
        await db.MatchVideoAnalyses
            .Where(a => a.Status == VideoAnalysisStatus.Running || (a.Status == VideoAnalysisStatus.Paused && a.WorkerId != null))
            .ToListAsync(cancellationToken);

    public Task<VideoAnalysisControlState?> GetControlAsync(Guid analysisId, CancellationToken cancellationToken = default) =>
        db.MatchVideoAnalyses.AsNoTracking()
            .Where(a => a.Id == analysisId)
            .Select(a => new VideoAnalysisControlState(a.Status, a.WorkerId))
            .FirstOrDefaultAsync(cancellationToken);

    // El borrado de los recortes anteriores va con SQL directo: dentro de la misma transacción que el guardado, para que
    // un fallo a medias no deje el análisis sin recortes (o con los de otra pasada).
    public async Task SaveResultAsync(Guid analysisId, IReadOnlyCollection<VideoAnalysisCrop> crops, CancellationToken cancellationToken = default)
    {
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.VideoAnalysisCrops.Where(c => c.AnalysisId == analysisId).ExecuteDeleteAsync(cancellationToken);
            foreach (var crop in crops)
                if (db.Entry(crop).State == EntityState.Detached) db.VideoAnalysisCrops.Add(crop);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    public async Task<bool> DiscardChangesAsync(MatchVideoAnalysis analysis, CancellationToken cancellationToken = default)
    {
        foreach (var pending in db.ChangeTracker.Entries<VideoAnalysisCrop>().Where(e => e.Entity.AnalysisId == analysis.Id && e.State == EntityState.Added).ToList())
            pending.State = EntityState.Detached;

        var entry = db.Entry(analysis);
        if (entry.State == EntityState.Detached) return false;
        await entry.ReloadAsync(cancellationToken);
        return entry.State != EntityState.Detached;
    }

    public Task<byte[]?> GetCropAsync(Guid analysisId, int tracklet, CancellationToken cancellationToken = default) =>
        db.VideoAnalysisCrops.AsNoTracking()
            .Where(c => c.AnalysisId == analysisId && c.Tracklet == tracklet)
            .Select(c => c.Content)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlySet<int>> GetCropTrackletsAsync(Guid analysisId, CancellationToken cancellationToken = default) =>
        (await db.VideoAnalysisCrops.AsNoTracking().Where(c => c.AnalysisId == analysisId).Select(c => c.Tracklet).ToListAsync(cancellationToken)).ToHashSet();
}
