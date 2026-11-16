using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Alerts;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Players;
using TercerTiempo.Infrastructure.Persistence;
using TercerTiempo.Infrastructure.Persistence.Configurations;

namespace TercerTiempo.Infrastructure.Alerts;

internal sealed class AlertStore(AppDbContext db) : IAlertStore
{
    public Task<Alert?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Set<Alert>().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Alert>> GetByClubAsync(Guid clubId, CancellationToken cancellationToken = default) =>
        await db.Set<Alert>().Where(a => a.ClubId == clubId).OrderByDescending(a => a.CreatedAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Alert>> GetForAsync(Guid? clubId, Guid? playerId, Guid? coachId, CancellationToken cancellationToken = default)
    {
        if (clubId is null && playerId is null && coachId is null) return [];
        return await db.Set<Alert>()
            .Where(a => (clubId != null && a.ClubId == clubId) || (playerId != null && a.PlayerId == playerId) || (coachId != null && a.CoachId == coachId))
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Alert>> GetByKindAsync(AlertKind kind, CancellationToken cancellationToken = default) =>
        await db.Set<Alert>().Where(a => a.Kind == kind).ToListAsync(cancellationToken);

    public async Task<AlertPage> GetPageForAsync(Guid? clubId, Guid? playerId, Guid? coachId, int take, CancellationToken cancellationToken = default)
    {
        if (clubId is null && playerId is null && coachId is null) return new AlertPage(0, []);
        var mine = For(clubId, playerId, coachId);
        var unread = await mine.CountAsync(a => a.ReadAt == null && a.ResolvedAt == null, cancellationToken);
        var latest = await mine.AsNoTracking().OrderByDescending(a => a.CreatedAt).Take(take).ToListAsync(cancellationToken);
        return new AlertPage(unread, latest);
    }

    public async Task<IReadOnlyList<Alert>> GetOpenByKeyPrefixAsync(
        string keyPrefix, IReadOnlyCollection<Guid> playerIds, IReadOnlyCollection<Guid> coachIds, Guid? clubId, CancellationToken cancellationToken = default)
    {
        if (playerIds.Count == 0 && coachIds.Count == 0 && clubId is null) return [];
        var found = await db.Set<Alert>()
            .Where(a => a.ResolvedAt == null && a.Key.StartsWith(keyPrefix))
            .Where(a => (a.PlayerId != null && playerIds.Contains(a.PlayerId.Value))
                || (a.CoachId != null && coachIds.Contains(a.CoachId.Value))
                || (clubId != null && a.ClubId == clubId))
            .ToListAsync(cancellationToken);
        // LIKE no distingue mayúsculas con la intercalación de la base; la clave se compara además tal cual.
        return found.Where(a => a.Key.StartsWith(keyPrefix, StringComparison.Ordinal)).ToList();
    }

    public Task<int> MarkAllReadAsync(Guid? clubId, Guid? playerId, Guid? coachId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        clubId is null && playerId is null && coachId is null
            ? Task.FromResult(0)
            : For(clubId, playerId, coachId).Where(a => a.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(a => a.ReadAt, now), cancellationToken);

    public Task<int> DeleteResolvedBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken = default) =>
        db.Set<Alert>().Where(a => a.ResolvedAt != null && a.ResolvedAt < before).ExecuteDeleteAsync(cancellationToken);

    private IQueryable<Alert> For(Guid? clubId, Guid? playerId, Guid? coachId) =>
        db.Set<Alert>().Where(a => (clubId != null && a.ClubId == clubId) || (playerId != null && a.PlayerId == playerId) || (coachId != null && a.CoachId == coachId));

    public void Add(Alert alert) => db.Set<Alert>().Add(alert);
}

internal sealed class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> builder)
    {
        builder.ToTable("Alerts");
        builder.ConfigureAggregate();
        builder.Ignore(a => a.IsRead);
        builder.Ignore(a => a.IsResolved);

        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(40);
        builder.Property(a => a.Key).HasMaxLength(120).IsRequired();
        builder.Property(a => a.Title).HasMaxLength(150).IsRequired();
        builder.Property(a => a.Message).HasMaxLength(500).IsRequired();
        builder.Property(a => a.Link).HasMaxLength(200);

        // Un destinatario por aviso: al borrar la cuenta se van sus avisos.
        builder.HasOne<Club>().WithMany().HasForeignKey(a => a.ClubId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Player>().WithMany().HasForeignKey(a => a.PlayerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Coach>().WithMany().HasForeignKey(a => a.CoachId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => new { a.ClubId, a.PlayerId, a.CoachId, a.Key }).IsUnique();
        builder.HasIndex(a => new { a.ClubId, a.CreatedAt });
        builder.HasIndex(a => new { a.PlayerId, a.CreatedAt });
        builder.HasIndex(a => new { a.CoachId, a.CreatedAt });
        // Para resolver los avisos de jugadores y técnicos por clave (convocatorias, citaciones, faltas).
        builder.HasIndex(a => new { a.PlayerId, a.Key });
        builder.HasIndex(a => new { a.CoachId, a.Key });
    }
}
