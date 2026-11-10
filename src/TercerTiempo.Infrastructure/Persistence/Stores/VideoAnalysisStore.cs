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

    public void Add(MatchVideoAnalysis analysis) => db.MatchVideoAnalyses.Add(analysis);

    public void Remove(MatchVideoAnalysis analysis) => db.MatchVideoAnalyses.Remove(analysis);

    public Task<MatchVideoAnalysis?> GetNextPendingAsync(int maxAttempts, CancellationToken cancellationToken = default) =>
        db.MatchVideoAnalyses
            .Where(a => a.Status == VideoAnalysisStatus.Pending && a.Attempts < maxAttempts
                && db.MatchVideos.Any(v => v.Id == a.VideoId && v.DeletedAt == null))
            .OrderBy(a => a.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<MatchVideoAnalysis>> GetRunningAsync(CancellationToken cancellationToken = default) =>
        await db.MatchVideoAnalyses.Where(a => a.Status == VideoAnalysisStatus.Running).ToListAsync(cancellationToken);

    // Los recortes de la pasada anterior se van en el momento; los nuevos se guardan con el resto de la unidad de trabajo.
    public async Task ReplaceCropsAsync(Guid analysisId, IReadOnlyCollection<VideoAnalysisCrop> crops, CancellationToken cancellationToken = default)
    {
        await db.VideoAnalysisCrops.Where(c => c.AnalysisId == analysisId).ExecuteDeleteAsync(cancellationToken);
        db.VideoAnalysisCrops.AddRange(crops);
    }

    public Task<byte[]?> GetCropAsync(Guid analysisId, int tracklet, CancellationToken cancellationToken = default) =>
        db.VideoAnalysisCrops.AsNoTracking()
            .Where(c => c.AnalysisId == analysisId && c.Tracklet == tracklet)
            .Select(c => c.Content)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlySet<int>> GetCropTrackletsAsync(Guid analysisId, CancellationToken cancellationToken = default) =>
        (await db.VideoAnalysisCrops.AsNoTracking().Where(c => c.AnalysisId == analysisId).Select(c => c.Tracklet).ToListAsync(cancellationToken)).ToHashSet();
}
