using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Infrastructure.Persistence;
using TercerTiempo.Infrastructure.Persistence.Configurations;

namespace TercerTiempo.Infrastructure.FootballData;

internal sealed class FootballSyncSettingsStore(AppDbContext db) : IFootballSyncSettingsStore
{
    public Task<FootballSyncSettings?> GetAsync(CancellationToken cancellationToken = default) =>
        db.FootballSyncSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

    public Task<FootballSyncSettings?> GetForUpdateAsync(CancellationToken cancellationToken = default) =>
        db.FootballSyncSettings.FirstOrDefaultAsync(cancellationToken);

    public void Add(FootballSyncSettings settings) => db.FootballSyncSettings.Add(settings);
}

internal sealed class FootballSyncSettingsConfiguration : IEntityTypeConfiguration<FootballSyncSettings>
{
    public void Configure(EntityTypeBuilder<FootballSyncSettings> builder)
    {
        builder.ToTable("FootballSyncSettings");
        builder.ConfigureAggregate();
        builder.Property(s => s.ScheduledScope).HasConversion<string>().HasMaxLength(20);

        builder.HasData(new
        {
            Id = FootballSyncSettings.SingletonId,
            ScheduledScope = SyncScopeKind.CurrentRound,
            DaysBack = 3,
            DaysAhead = 4,
            RostersInRound = false,
            FullSyncEveryDays = 7,
            UpdatedAt = new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero),
        });
    }
}
