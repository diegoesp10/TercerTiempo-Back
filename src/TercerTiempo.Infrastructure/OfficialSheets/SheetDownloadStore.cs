using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Infrastructure.Persistence;

namespace TercerTiempo.Infrastructure.OfficialSheets;

// Una fila por partido cuya acta se ha pedido desde la administración.
internal sealed class SheetDownload
{
    public Guid MatchId { get; set; }
    public string Status { get; set; } = SheetDownloadStatus.Pending;
    public long Sequence { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? Detail { get; set; }
    public int Attempts { get; set; }
}

internal sealed class SheetDownloadConfiguration : IEntityTypeConfiguration<SheetDownload>
{
    public void Configure(EntityTypeBuilder<SheetDownload> b)
    {
        b.ToTable("SheetDownloads");
        b.HasKey(x => x.MatchId);
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.Detail).HasMaxLength(500);
        b.HasOne<Match>().WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.Status, x.Sequence });
    }
}

internal sealed class SheetDownloadStore(AppDbContext db) : ISheetDownloadStore
{
    private static readonly string[] Finished = [SheetDownloadStatus.Done, SheetDownloadStatus.Missing, SheetDownloadStatus.Failed];

    public async Task<int> RequestAsync(IReadOnlyList<Guid> matchIds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var ids = matchIds.Distinct().ToList();
        var existing = await db.Set<SheetDownload>().Where(d => ids.Contains(d.MatchId)).ToDictionaryAsync(d => d.MatchId, cancellationToken);
        var sequence = (await db.Set<SheetDownload>().MaxAsync(d => (long?)d.Sequence, cancellationToken) ?? 0) + 1;

        var queued = 0;
        foreach (var id in ids)
        {
            if (existing.TryGetValue(id, out var row))
            {
                if (row.Status is SheetDownloadStatus.Pending or SheetDownloadStatus.Downloading) continue;
                row.Status = SheetDownloadStatus.Pending;
                row.Detail = null;
                row.StartedAt = null;
                row.FinishedAt = null;
            }
            else
            {
                row = new SheetDownload { MatchId = id };
                db.Add(row);
            }
            row.Sequence = sequence++;
            row.RequestedAt = now;
            queued++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return queued;
    }

    public Task<Guid?> NextPendingAsync(CancellationToken cancellationToken) =>
        db.Set<SheetDownload>().AsNoTracking()
            .Where(d => d.Status == SheetDownloadStatus.Pending)
            .OrderBy(d => d.Sequence)
            .Select(d => (Guid?)d.MatchId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task MarkAsync(Guid matchId, string status, string? detail, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var row = await db.Set<SheetDownload>().SingleOrDefaultAsync(d => d.MatchId == matchId, cancellationToken);
        if (row is null) return;

        row.Status = status;
        row.Detail = detail is { Length: > 500 } ? detail[..500] : detail;
        if (status == SheetDownloadStatus.Downloading)
        {
            row.StartedAt = now;
            row.Attempts++;
        }
        row.FinishedAt = Finished.Contains(status) ? now : null;
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task ResetInterruptedAsync(CancellationToken cancellationToken) =>
        db.Set<SheetDownload>()
            .Where(d => d.Status == SheetDownloadStatus.Downloading)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, SheetDownloadStatus.Pending), cancellationToken);

    public async Task<IReadOnlyList<SheetDownloadRow>> GetAllAsync(CancellationToken cancellationToken) =>
        await db.Set<SheetDownload>().AsNoTracking()
            .Select(d => new SheetDownloadRow(d.MatchId, d.Status, d.Sequence, d.RequestedAt, d.StartedAt, d.FinishedAt, d.Detail, d.Attempts))
            .ToListAsync(cancellationToken);

    public Task<int> CancelPendingAsync(CancellationToken cancellationToken) =>
        db.Set<SheetDownload>().Where(d => d.Status == SheetDownloadStatus.Pending).ExecuteDeleteAsync(cancellationToken);

    public Task<int> ClearFinishedAsync(CancellationToken cancellationToken) =>
        db.Set<SheetDownload>().Where(d => Finished.Contains(d.Status)).ExecuteDeleteAsync(cancellationToken);
}
