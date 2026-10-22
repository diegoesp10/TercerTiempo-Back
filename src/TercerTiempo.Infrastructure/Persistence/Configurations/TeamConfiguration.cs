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
        builder.Property(t => t.CrestKey).HasMaxLength(80);

        builder.OwnsExternalRef(t => t.ExternalRef);
        builder.OwnsExternalRef(t => t.OfficialRef, "Official");

        // Ficha de la federación en la misma tabla, con prefijo Profile_. La navegación es obligatoria (la ficha existe
        // siempre, vacía si no se ha descargado) para que EF no deduzca su existencia de las columnas nulas; se marca
        // después de OwnsOne, que es cuando EF ya sabe quién es el dependiente.
        builder.OwnsOne(t => t.Profile, profile =>
        {
            profile.Property(p => p.OfficialName).HasColumnName("Profile_OfficialName").HasMaxLength(160);
            profile.Property(p => p.Locality).HasColumnName("Profile_Locality").HasMaxLength(120);
            profile.Property(p => p.Province).HasColumnName("Profile_Province").HasMaxLength(80);
            profile.Property(p => p.Address).HasColumnName("Profile_Address").HasMaxLength(200);
            profile.Property(p => p.PostalCode).HasColumnName("Profile_PostalCode").HasMaxLength(12);
            profile.Property(p => p.Website).HasColumnName("Profile_Website").HasMaxLength(200);
            profile.Property(p => p.Ground).HasColumnName("Profile_Ground").HasMaxLength(160);
            profile.Property(p => p.GroundSurface).HasColumnName("Profile_GroundSurface").HasMaxLength(60);
            profile.Property(p => p.GroundAddress).HasColumnName("Profile_GroundAddress").HasMaxLength(200);
            profile.Property(p => p.GroundLocality).HasColumnName("Profile_GroundLocality").HasMaxLength(120);
            profile.Property(p => p.ShirtColor).HasColumnName("Profile_ShirtColor").HasMaxLength(60);
            profile.Property(p => p.ShortsColor).HasColumnName("Profile_ShortsColor").HasMaxLength(60);
            profile.Property(p => p.SocksColor).HasColumnName("Profile_SocksColor").HasMaxLength(60);
        });
        builder.Navigation(t => t.Profile).IsRequired();
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
        builder.Property(r => r.PhotoKey).HasMaxLength(100);

        builder.HasOne<Team>().WithMany().HasForeignKey(r => r.TeamId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Season>().WithMany().HasForeignKey(r => r.SeasonId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => new { r.TeamId, r.SeasonId });
        builder.HasIndex(r => new { r.SeasonId, r.NormalizedName });

        builder.OwnsExternalRef(r => r.ExternalRef);
    }
}
