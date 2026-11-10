using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Competitions;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

internal sealed class SeasonZoneStore(AppDbContext db) : ISeasonZoneStore
{
    public async Task<IReadOnlyList<SeasonZone>> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default) =>
        await db.Set<SeasonZone>().Where(z => z.SeasonId == seasonId).OrderBy(z => z.Position).ToListAsync(cancellationToken);

    public void Add(SeasonZone zone) => db.Set<SeasonZone>().Add(zone);

    public void Remove(SeasonZone zone) => db.Set<SeasonZone>().Remove(zone);
}

internal sealed class SeasonZoneConfiguration : IEntityTypeConfiguration<SeasonZone>
{
    public void Configure(EntityTypeBuilder<SeasonZone> builder)
    {
        builder.ToTable("SeasonZones");
        builder.HasKey(z => new { z.SeasonId, z.Position });
        builder.Property(z => z.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(z => z.Label).HasMaxLength(SeasonZone.MaxLabelLength).IsRequired();
        builder.Property(z => z.Color).HasMaxLength(7).IsRequired();
        builder.HasOne<Season>().WithMany().HasForeignKey(z => z.SeasonId).OnDelete(DeleteBehavior.Cascade);
    }
}
