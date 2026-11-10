using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Squads;
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

        // Una sola ruta en cascada (la ficha); SQL Server rechaza varias rutas hacia la misma tabla.
        builder.HasOne<SquadPlayer>().WithMany().HasForeignKey(p => p.SquadPlayerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Squad>().WithMany().HasForeignKey(p => p.SquadId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Match>().WithMany().HasForeignKey(p => p.MatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Team>().WithMany().HasForeignKey(p => p.TeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Season>().WithMany().HasForeignKey(p => p.SeasonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Coach>().WithMany().HasForeignKey(p => p.RecordedByCoachId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Club>().WithMany().HasForeignKey(p => p.RecordedByClubId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.SquadPlayerId, p.MatchId }).IsUnique();
        builder.HasIndex(p => new { p.SquadId, p.MatchId });
        builder.HasIndex(p => p.SeasonId);
        builder.HasIndex(p => p.MatchId);
    }
}
