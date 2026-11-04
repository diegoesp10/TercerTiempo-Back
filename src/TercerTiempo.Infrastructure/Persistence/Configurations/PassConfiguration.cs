using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Passes;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Infrastructure.Persistence.Configurations;

internal sealed class MatchPassConfiguration : IEntityTypeConfiguration<MatchPass>
{
    public void Configure(EntityTypeBuilder<MatchPass> builder)
    {
        builder.ToTable("MatchPasses");
        builder.ConfigureAggregate();
        builder.Ignore(p => p.IsRedeemed);
        builder.Ignore(p => p.IsRevoked);
        builder.Ignore(p => p.IsFree);

        builder.Property(p => p.Code).HasMaxLength(MatchPass.CodeLength).IsRequired();
        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        builder.Property(p => p.Recipient).HasMaxLength(100);
        builder.Property(p => p.RedeemedByName).HasMaxLength(150);

        // Dos personas canjeando el mismo código a la vez: la segunda escritura falla en lugar de pisar a la primera.
        builder.Property<byte[]>("Version").IsRowVersion();

        builder.HasOne<Squad>().WithMany().HasForeignKey(p => p.SquadId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Team>().WithMany().HasForeignKey(p => p.TeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Match>().WithMany().HasForeignKey(p => p.MatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Club>().WithMany().HasForeignKey(p => p.ClubId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Coach>().WithMany().HasForeignKey(p => p.CreatedByCoachId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Club>().WithMany().HasForeignKey(p => p.CreatedByClubId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.Code).IsUnique();
        builder.HasIndex(p => new { p.SquadId, p.MatchId });
        builder.HasIndex(p => p.RedeemedByUserId);
        builder.HasIndex(p => p.ForUserId);
    }
}

internal sealed class MatchPassRequestConfiguration : IEntityTypeConfiguration<MatchPassRequest>
{
    public void Configure(EntityTypeBuilder<MatchPassRequest> builder)
    {
        builder.ToTable("MatchPassRequests");
        builder.ConfigureAggregate();
        builder.Ignore(r => r.IsPending);

        builder.Property(r => r.RequesterName).HasMaxLength(150).IsRequired();
        builder.Property(r => r.Message).HasMaxLength(300);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<Squad>().WithMany().HasForeignKey(r => r.SquadId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Team>().WithMany().HasForeignKey(r => r.TeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Match>().WithMany().HasForeignKey(r => r.MatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Club>().WithMany().HasForeignKey(r => r.ClubId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.ClubId);
        builder.HasIndex(r => r.RequesterUserId);
    }
}

internal sealed class MatchPassPriceConfiguration : IEntityTypeConfiguration<MatchPassPrice>
{
    public void Configure(EntityTypeBuilder<MatchPassPrice> builder)
    {
        builder.ToTable("MatchPassPrices");
        builder.ConfigureAggregate();
        builder.Ignore(p => p.IsEmpty);

        builder.HasOne<Squad>().WithMany().HasForeignKey(p => p.SquadId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Match>().WithMany().HasForeignKey(p => p.MatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Club>().WithMany().HasForeignKey(p => p.ClubId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.SquadId, p.MatchId }).IsUnique();
        builder.HasIndex(p => p.ClubId);
    }
}

// Las ventas son un registro contable: no dependen de que el pase, la plantilla o el club sigan existiendo.
internal sealed class PassSaleConfiguration : IEntityTypeConfiguration<PassSale>
{
    public void Configure(EntityTypeBuilder<PassSale> builder)
    {
        builder.ToTable("PassSales");
        builder.ConfigureAggregate();

        builder.Property(s => s.Currency).HasMaxLength(3).IsRequired();
        builder.Property(s => s.Provider).HasMaxLength(30).IsRequired();
        builder.Property(s => s.PaymentReference).HasMaxLength(200).IsRequired();

        builder.HasIndex(s => s.PassId).IsUnique();
        builder.HasIndex(s => s.ClubId);
    }
}
