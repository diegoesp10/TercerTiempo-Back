using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Licensing;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Infrastructure.Persistence;
using TercerTiempo.Infrastructure.Persistence.Configurations;

namespace TercerTiempo.Infrastructure.Licensing;

// Pasarela simulada: no cobra nada. Devuelve una "sesión" que el frontal muestra como página de pago de prueba y que
// se confirma con un clic; las renovaciones las cobra el trabajo de renovaciones. Stripe ocupa su sitio con
// Licensing:Provider = "stripe" sin tocar el resto.
internal sealed class MockLicensePaymentGateway : ILicensePaymentGateway
{
    public string Provider => "mock";

    public bool RenewsByWebhook => false;

    public Task<CheckoutSession> BeginCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken)
    {
        var sessionId = $"mock_{Guid.NewGuid():N}";
        return Task.FromResult(new CheckoutSession(sessionId, $"/pago/{sessionId}"));
    }

    public Task<CheckoutResult> GetCheckoutAsync(string sessionId, CancellationToken cancellationToken)
    {
        var completed = sessionId.StartsWith("mock_", StringComparison.Ordinal);
        return Task.FromResult(new CheckoutResult(completed, completed ? $"mocksub_{sessionId[5..]}" : null, null, null));
    }

    public Task CancelRenewalAsync(string subscriptionRef, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ResumeRenewalAsync(string subscriptionRef, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ChangeRenewalAsync(string subscriptionRef, LicensePlan plan, BillingInterval interval, CancellationToken cancellationToken) => Task.CompletedTask;

    public LicenseWebhookEvent? ParseWebhook(string payload, string? signature) => null;
}

internal sealed class LicenseStore(AppDbContext db) : ILicenseStore
{
    public Task<TeamLicense?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.TeamLicenses.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

    public Task<TeamLicense?> GetByTeamAsync(Guid teamId, CancellationToken cancellationToken = default) =>
        db.TeamLicenses.FirstOrDefaultAsync(l => l.TeamId == teamId, cancellationToken);

    public Task<TeamLicense?> GetBySubscriptionAsync(string subscriptionRef, CancellationToken cancellationToken = default) =>
        db.TeamLicenses.FirstOrDefaultAsync(l => l.SubscriptionRef == subscriptionRef, cancellationToken);

    public async Task<IReadOnlyList<TeamLicense>> GetByClubAsync(Guid clubId, CancellationToken cancellationToken = default) =>
        await db.TeamLicenses.Where(l => l.ClubId == clubId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TeamLicense>> GetRenewingAsync(string provider, CancellationToken cancellationToken = default) =>
        await db.TeamLicenses.Where(l => l.AutoRenew && l.Plan != null && l.Provider == provider).ToListAsync(cancellationToken);

    public void Add(TeamLicense license) => db.TeamLicenses.Add(license);

    public Task<LicensePayment?> GetPaymentBySessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
        db.LicensePayments.FirstOrDefaultAsync(p => p.SessionId == sessionId, cancellationToken);

    public async Task<IReadOnlyList<LicensePayment>> GetPaymentsByClubAsync(Guid clubId, CancellationToken cancellationToken = default) =>
        await db.LicensePayments.Where(p => p.ClubId == clubId).OrderByDescending(p => p.CreatedAt).ToListAsync(cancellationToken);

    public void AddPayment(LicensePayment payment) => db.LicensePayments.Add(payment);
}

internal sealed class TeamLicenseConfiguration : IEntityTypeConfiguration<TeamLicense>
{
    public void Configure(EntityTypeBuilder<TeamLicense> builder)
    {
        builder.ToTable("TeamLicenses");
        builder.ConfigureAggregate();
        builder.Ignore(l => l.ValidUntil);
        builder.Ignore(l => l.IsRenewing);
        builder.Ignore(l => l.RenewsOn);
        builder.Ignore(l => l.EffectivePlan);
        builder.Ignore(l => l.StaffPlan);

        builder.Property(l => l.Plan).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.Interval).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.NextPlan).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.NextInterval).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.Provider).HasMaxLength(40);
        builder.Property(l => l.CustomerRef).HasMaxLength(120);
        builder.Property(l => l.SubscriptionRef).HasMaxLength(120);

        builder.HasOne<Club>().WithMany().HasForeignKey(l => l.ClubId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Team>().WithMany().HasForeignKey(l => l.TeamId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => l.TeamId).IsUnique();
        builder.HasIndex(l => l.ClubId);
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
        builder.Property(p => p.Plan).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Interval).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<TeamLicense>().WithMany().HasForeignKey(p => p.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Club>().WithMany().HasForeignKey(p => p.ClubId).OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(p => p.SessionId).IsUnique();
        builder.HasIndex(p => p.ClubId);
    }
}
