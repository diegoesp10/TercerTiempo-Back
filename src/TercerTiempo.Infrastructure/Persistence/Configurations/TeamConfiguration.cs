using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Infrastructure.Persistence.Configurations;

internal sealed class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.ToTable("Teams");
        builder.ConfigureAggregate();

        builder.Property(t => t.Name).HasMaxLength(120).IsRequired();
        builder.Property(t => t.ShortName).HasMaxLength(30).IsRequired();
        builder.Property(t => t.CrestUrl).HasMaxLength(500);

        builder.OwnsExternalRef(t => t.ExternalRef);
    }
}

internal sealed class RosterEntryConfiguration : IEntityTypeConfiguration<RosterEntry>
{
    public void Configure(EntityTypeBuilder<RosterEntry> builder)
    {
        builder.ToTable("RosterEntries");
        builder.ConfigureAggregate();

        builder.Property(r => r.FullName).HasMaxLength(150).IsRequired();
        builder.Property(r => r.NormalizedName).HasMaxLength(150).IsRequired();
        builder.Property(r => r.Position).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<Team>().WithMany().HasForeignKey(r => r.TeamId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Season>().WithMany().HasForeignKey(r => r.SeasonId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => new { r.TeamId, r.SeasonId });
        builder.HasIndex(r => new { r.SeasonId, r.NormalizedName });

        builder.OwnsExternalRef(r => r.ExternalRef);
    }
}
