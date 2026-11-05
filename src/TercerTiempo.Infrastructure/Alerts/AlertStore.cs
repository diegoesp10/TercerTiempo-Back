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
    }
}
