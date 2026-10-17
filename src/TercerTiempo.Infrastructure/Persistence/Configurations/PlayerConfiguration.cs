using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Infrastructure.Persistence.Configurations;

internal sealed class PlayerConfiguration : IEntityTypeConfiguration<Player>
{
    public void Configure(EntityTypeBuilder<Player> builder)
    {
        builder.ToTable("Players");
        builder.ConfigureAggregate();

        builder.Property(p => p.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.NormalizedName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Position).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(p => p.UserId).IsUnique();
        builder.HasIndex(p => p.NormalizedName);

        builder.OwnsExternalRef(p => p.ExternalRef);

        builder.HasMany(p => p.Memberships)
            .WithOne()
            .HasForeignKey(m => m.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(p => p.Memberships)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();
    }
}

internal sealed class SquadMembershipConfiguration : IEntityTypeConfiguration<SquadMembership>
{
    public void Configure(EntityTypeBuilder<SquadMembership> builder)
    {
        builder.ToTable("SquadMemberships");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<Team>().WithMany().HasForeignKey(m => m.TeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Season>().WithMany().HasForeignKey(m => m.SeasonId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => new { m.PlayerId, m.SeasonId });
        builder.HasIndex(m => new { m.TeamId, m.SeasonId });
    }
}
