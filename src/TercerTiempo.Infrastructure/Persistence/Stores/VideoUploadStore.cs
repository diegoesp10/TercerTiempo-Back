using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

internal sealed class VideoUploadStore(AppDbContext db) : IVideoUploadStore
{
    public Task<VideoUploadSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.VideoUploads.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<VideoUploadSession>> GetByOwnerAsync(Guid? coachId, Guid? clubId, CancellationToken cancellationToken = default) =>
        await db.VideoUploads.Where(s => (coachId != null && s.CoachId == coachId) || (clubId != null && s.ClubId == clubId)).ToListAsync(cancellationToken);

    public void Add(VideoUploadSession session) => db.VideoUploads.Add(session);

    public void Remove(VideoUploadSession session) => db.VideoUploads.Remove(session);
}
