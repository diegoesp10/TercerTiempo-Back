using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Infrastructure.Persistence.Configurations;

internal sealed class PerformanceConfiguration : IEntityTypeConfiguration<PlayerMatchPerformance>
{
    public void Configure(EntityTypeBuilder<PlayerMatchPerformance> builder)
    {
        builder.ToTable("PlayerMatchPerformances");
        builder.ConfigureAggregate();
        builder.Ignore(p => p.GoalContributions);

        builder.Property(p => p.Notes).HasMaxLength(PerformanceStats.MaxNotesLength);

        builder.HasOne<Player>().WithMany().HasForeignKey(p => p.PlayerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Match>().WithMany().HasForeignKey(p => p.MatchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Team>().WithMany().HasForeignKey(p => p.TeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Season>().WithMany().HasForeignKey(p => p.SeasonId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => new { p.PlayerId, p.MatchId }).IsUnique();
        builder.HasIndex(p => new { p.PlayerId, p.SeasonId });
        builder.HasIndex(p => new { p.TeamId, p.SeasonId });
        builder.HasIndex(p => p.MatchId);
    }
}
