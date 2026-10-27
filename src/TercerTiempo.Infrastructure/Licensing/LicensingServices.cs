using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Licensing;
using TercerTiempo.Infrastructure.Persistence;
using TercerTiempo.Infrastructure.Persistence.Configurations;

namespace TercerTiempo.Infrastructure.Licensing;

internal sealed class LicenseStore(AppDbContext db) : ILicenseStore
{
    public Task<ClubLicense?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.ClubLicenses.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

    public Task<ClubLicense?> GetByClubAsync(Guid clubId, CancellationToken cancellationToken = default) =>
        db.ClubLicenses.FirstOrDefaultAsync(l => l.ClubId == clubId, cancellationToken);

    public Task<ClubLicense?> GetBySubscriptionAsync(string subscriptionRef, CancellationToken cancellationToken = default) =>
        db.ClubLicenses.FirstOrDefaultAsync(l => l.SubscriptionRef == subscriptionRef, cancellationToken);

    public async Task<IReadOnlyList<ClubLicense>> GetRenewingAsync(string provider, CancellationToken cancellationToken = default) =>
        await db.ClubLicenses.Where(l => l.AutoRenew && l.Interval != null && l.Provider == provider).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ClubLicense>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.ClubLicenses.AsNoTracking().ToListAsync(cancellationToken);

    public void Add(ClubLicense license) => db.ClubLicenses.Add(license);

    public Task<LicensePayment?> GetPaymentBySessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
        db.LicensePayments.FirstOrDefaultAsync(p => p.SessionId == sessionId, cancellationToken);

    public async Task<IReadOnlyList<LicensePayment>> GetPaymentsByClubAsync(Guid clubId, CancellationToken cancellationToken = default) =>
        await db.LicensePayments.Where(p => p.ClubId == clubId).OrderByDescending(p => p.CreatedAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LicensePayment>> GetAllPaymentsAsync(CancellationToken cancellationToken = default) =>
        await db.LicensePayments.AsNoTracking().ToListAsync(cancellationToken);

    public void AddPayment(LicensePayment payment) => db.LicensePayments.Add(payment);
}

internal sealed class ClubLicenseConfiguration : IEntityTypeConfiguration<ClubLicense>
{
    public void Configure(EntityTypeBuilder<ClubLicense> builder)
    {
        builder.ToTable("ClubLicenses");
        builder.ConfigureAggregate();
        builder.Ignore(l => l.ValidUntil);
        builder.Ignore(l => l.IsRenewing);
        builder.Ignore(l => l.RenewsOn);
        builder.Ignore(l => l.IsContracted);
        builder.Ignore(l => l.RenewalStaffSeats);

        builder.Property(l => l.Interval).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.NextInterval).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.Provider).HasMaxLength(40);
        builder.Property(l => l.CustomerRef).HasMaxLength(120);
        builder.Property(l => l.SubscriptionRef).HasMaxLength(120);

        builder.HasOne<Club>().WithMany().HasForeignKey(l => l.ClubId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(l => l.ClubId).IsUnique();
        builder.HasIndex(l => l.SubscriptionRef);
    }
}

internal sealed class LicensePaymentConfiguration : IEntityTypeConfiguration<LicensePayment>
{
    public void Configure(EntityTypeBuilder<LicensePayment> builder)
    {
        builder.ToTable("LicensePayments");
        builder.ConfigureAggregate();
        builder.Ignore(p => p.IsPaid);

        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        builder.Property(p => p.Provider).HasMaxLength(40).IsRequired();
        builder.Property(p => p.SessionId).HasMaxLength(120).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Interval).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<ClubLicense>().WithMany().HasForeignKey(p => p.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Club>().WithMany().HasForeignKey(p => p.ClubId).OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(p => p.SessionId).IsUnique();
        builder.HasIndex(p => p.ClubId);
    }
}
