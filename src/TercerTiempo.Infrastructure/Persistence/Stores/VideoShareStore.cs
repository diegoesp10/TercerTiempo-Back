using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

internal sealed class VideoShareStore(AppDbContext db) : IVideoShareStore
{
    public Task<VideoShareLink?> GetByTokenAsync(string token, CancellationToken cancellationToken = default) =>
        db.VideoShareLinks.FirstOrDefaultAsync(l => l.Token == token, cancellationToken);

    public async Task<IReadOnlyList<VideoShareLink>> GetByVideoAsync(Guid videoId, CancellationToken cancellationToken = default) =>
        await db.VideoShareLinks.Where(l => l.VideoId == videoId).ToListAsync(cancellationToken);

    public void Add(VideoShareLink link) => db.VideoShareLinks.Add(link);
}
