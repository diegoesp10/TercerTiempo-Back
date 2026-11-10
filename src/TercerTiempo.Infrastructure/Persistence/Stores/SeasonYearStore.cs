using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Infrastructure.Persistence.Configurations;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

internal sealed class SeasonYearStore(AppDbContext db) : ISeasonYearStore
{
    public async Task<IReadOnlyList<SeasonYear>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.SeasonYears.AsNoTracking().OrderByDescending(s => s.StartsOn).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SeasonYear>> GetAllForUpdateAsync(CancellationToken cancellationToken = default) =>
        await db.SeasonYears.OrderByDescending(s => s.StartsOn).ToListAsync(cancellationToken);

    public void Add(SeasonYear seasonYear) => db.SeasonYears.Add(seasonYear);
}

internal sealed class SeasonYearConfiguration : IEntityTypeConfiguration<SeasonYear>
{
    public void Configure(EntityTypeBuilder<SeasonYear> builder)
    {
        builder.ToTable("SeasonYears");
        builder.ConfigureAggregate();
        builder.Property(s => s.Label).HasMaxLength(20).IsRequired();
        builder.HasIndex(s => s.Label).IsUnique();

        // Solo una actual.
        builder.HasIndex(s => s.IsCurrent).IsUnique().HasFilter("[IsCurrent] = 1");
    }
}
