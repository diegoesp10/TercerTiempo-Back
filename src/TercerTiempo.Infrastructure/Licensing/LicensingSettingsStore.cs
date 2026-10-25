using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Licensing;
using TercerTiempo.Infrastructure.Persistence;
using TercerTiempo.Infrastructure.Persistence.Configurations;

namespace TercerTiempo.Infrastructure.Licensing;

internal sealed class LicensingSettingsStore(AppDbContext db) : ILicensingSettingsStore
{
    public Task<LicensingSettings?> GetAsync(CancellationToken cancellationToken = default) =>
        db.LicensingSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

    public Task<LicensingSettings?> GetForUpdateAsync(CancellationToken cancellationToken = default) =>
        db.LicensingSettings.FirstOrDefaultAsync(cancellationToken);

    public void Add(LicensingSettings settings) => db.LicensingSettings.Add(settings);
}

internal sealed class LicensingSettingsConfiguration : IEntityTypeConfiguration<LicensingSettings>
{
    public void Configure(EntityTypeBuilder<LicensingSettings> builder)
    {
        builder.ToTable("LicensingSettings");
        builder.ConfigureAggregate();
        builder.Property(s => s.Currency).HasMaxLength(3).IsRequired();

        builder.HasMany(s => s.Plans).WithOne().HasForeignKey(p => p.SettingsId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Plans).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        // Valores con los que se lanzó la app (antes estaban en appsettings.json).
        builder.HasData(new
        {
            Id = LicensingSettings.SingletonId,
            Currency = LicensingSettings.DefaultCurrency,
            TrialDays = 30,
            UpdatedAt = new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero),
        });
    }
}

internal sealed class LicensePlanSettingsConfiguration : IEntityTypeConfiguration<LicensePlanSettings>
{
    public void Configure(EntityTypeBuilder<LicensePlanSettings> builder)
    {
        builder.ToTable("LicensePlanSettings");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Plan).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.StripeMonthlyPriceId).HasMaxLength(100);
        builder.Property(p => p.StripeYearlyPriceId).HasMaxLength(100);
        builder.HasIndex(p => new { p.SettingsId, p.Plan }).IsUnique();

        builder.HasData(
            new
            {
                Id = new Guid("01a16f00-0000-7000-8000-000000000011"),
                SettingsId = LicensingSettings.SingletonId,
                Plan = LicensePlan.Basic,
                MonthlyPriceCents = 990,
                YearlyPriceCents = 9900,
                MaxStaff = 1,
            },
            new
            {
                Id = new Guid("01a16f00-0000-7000-8000-000000000012"),
                SettingsId = LicensingSettings.SingletonId,
                Plan = LicensePlan.Premium,
                MonthlyPriceCents = 1990,
                YearlyPriceCents = 19900,
                MaxStaff = 6,
            });
    }
}
