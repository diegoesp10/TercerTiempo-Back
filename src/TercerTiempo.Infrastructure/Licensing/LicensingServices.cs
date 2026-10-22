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
// se confirma con un clic. Stripe sustituirá esta clase (Checkout Session + verificación por webhook) sin tocar el resto.
internal sealed class MockLicensePaymentGateway : ILicensePaymentGateway
{
    public string Provider => "mock";

    public Task<CheckoutSession> BeginCheckoutAsync(Guid clubId, Guid teamId, int months, int amountCents, string currency, CancellationToken cancellationToken)
    {
        var sessionId = $"mock_{Guid.NewGuid():N}";
        return Task.FromResult(new CheckoutSession(sessionId, $"/pago/{sessionId}"));
    }

    public Task<bool> ConfirmAsync(string sessionId, CancellationToken cancellationToken) =>
        Task.FromResult(sessionId.StartsWith("mock_", StringComparison.Ordinal));
}

internal sealed class LicenseStore(AppDbContext db) : ILicenseStore
{
    public Task<TeamLicense?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.TeamLicenses.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

    public Task<TeamLicense?> GetByTeamAsync(Guid teamId, CancellationToken cancellationToken = default) =>
        db.TeamLicenses.FirstOrDefaultAsync(l => l.TeamId == teamId, cancellationToken);

    public async Task<IReadOnlyList<TeamLicense>> GetByClubAsync(Guid clubId, CancellationToken cancellationToken = default) =>
        await db.TeamLicenses.Where(l => l.ClubId == clubId).ToListAsync(cancellationToken);

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

        builder.HasOne<Club>().WithMany().HasForeignKey(l => l.ClubId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Team>().WithMany().HasForeignKey(l => l.TeamId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => l.TeamId).IsUnique();
        builder.HasIndex(l => l.ClubId);
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

        builder.HasOne<TeamLicense>().WithMany().HasForeignKey(p => p.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Club>().WithMany().HasForeignKey(p => p.ClubId).OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(p => p.SessionId).IsUnique();
        builder.HasIndex(p => p.ClubId);
    }
}
