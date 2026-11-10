using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Infrastructure.Persistence.Configurations;

internal sealed class MatchConfiguration : IEntityTypeConfiguration<Match>
{
    public void Configure(EntityTypeBuilder<Match> builder)
    {
        builder.ToTable("Matches");
        builder.ConfigureAggregate();

        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Venue).HasMaxLength(200);

        // Marcador: campos privados mapeados a columnas; la propiedad Score es calculada.
        builder.Property<int?>("_homeGoals").HasColumnName("HomeGoals");
        builder.Property<int?>("_awayGoals").HasColumnName("AwayGoals");
        builder.Ignore(m => m.Score);

        builder.HasOne<Season>().WithMany().HasForeignKey(m => m.SeasonId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Team>().WithMany().HasForeignKey(m => m.HomeTeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Team>().WithMany().HasForeignKey(m => m.AwayTeamId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => new { m.SeasonId, m.Matchday });
        builder.HasIndex(m => new { m.SeasonId, m.HomeTeamId });
        builder.HasIndex(m => new { m.SeasonId, m.AwayTeamId });
        builder.HasIndex(m => new { m.SeasonId, m.KickoffAt });

        builder.OwnsExternalRef(m => m.ExternalRef);
        builder.OwnsExternalRef(m => m.OfficialRef, "Official");
    }
}
