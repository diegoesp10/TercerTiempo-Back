using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

internal sealed class OfficialSheetStore(AppDbContext db, IOptions<PublicSheetOptions> options) : IOfficialSheetStore
{
    public async Task<OfficialMatchSheet?> GetByMatchAsync(Guid matchId, CancellationToken cancellationToken = default) =>
        await GetStoredByMatchAsync(matchId, cancellationToken) is { } sheet && options.Value.Allows(sheet.Provider) ? sheet : null;

    public async Task<IReadOnlyList<OfficialMatchSheet>> GetByMatchIdsAsync(IEnumerable<Guid> matchIds, CancellationToken cancellationToken = default) =>
        (await GetStoredByMatchIdsAsync(matchIds, cancellationToken)).Where(s => options.Value.Allows(s.Provider)).ToList();

    public Task<OfficialMatchSheet?> GetStoredByMatchAsync(Guid matchId, CancellationToken cancellationToken = default) =>
        db.OfficialSheets.FirstOrDefaultAsync(s => s.MatchId == matchId, cancellationToken);

    public async Task<IReadOnlyList<OfficialMatchSheet>> GetStoredByMatchIdsAsync(IEnumerable<Guid> matchIds, CancellationToken cancellationToken = default)
    {
        var list = matchIds.Distinct().ToList();
        if (list.Count == 0) return [];

        return await db.OfficialSheets.Where(s => list.Contains(s.MatchId)).ToListAsync(cancellationToken);
    }

    public void Add(OfficialMatchSheet sheet) => db.OfficialSheets.Add(sheet);
}

internal sealed class MatchVideoStore(AppDbContext db) : IMatchVideoStore
{
    public Task<MatchVideo?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.MatchVideos.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

    public async Task<IReadOnlyList<MatchVideo>> GetByMatchAsync(Guid matchId, CancellationToken cancellationToken = default) =>
        await db.MatchVideos.Where(v => v.MatchId == matchId && v.DeletedAt == null).ToListAsync(cancellationToken);

    public void Add(MatchVideo video) => db.MatchVideos.Add(video);
}

internal sealed class VideoPurchaseStore(AppDbContext db) : IVideoPurchaseStore
{
    public Task<VideoPurchase?> GetAsync(Guid videoId, Guid coachId, CancellationToken cancellationToken = default) =>
        db.VideoPurchases.FirstOrDefaultAsync(p => p.VideoId == videoId && p.CoachId == coachId, cancellationToken);

    public async Task<IReadOnlyList<VideoPurchase>> GetByCoachAsync(Guid coachId, CancellationToken cancellationToken = default) =>
        await db.VideoPurchases.Where(p => p.CoachId == coachId).ToListAsync(cancellationToken);

    public void Add(VideoPurchase purchase) => db.VideoPurchases.Add(purchase);
}
