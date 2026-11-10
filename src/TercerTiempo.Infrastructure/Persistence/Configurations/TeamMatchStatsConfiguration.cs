using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Infrastructure.Persistence.Configurations;

internal sealed class TeamMatchStatsConfiguration : IEntityTypeConfiguration<TeamMatchStats>
{
    public void Configure(EntityTypeBuilder<TeamMatchStats> builder)
    {
        builder.ToTable("TeamMatchStats");
        builder.ConfigureAggregate();

        builder.Property(s => s.Notes).HasMaxLength(TeamMatchStatsData.MaxNotesLength);
        // Con valor por defecto en SQL: una API anterior a estas columnas puede seguir guardando datos de equipo.
        builder.Property(s => s.GoalKicksFor).HasDefaultValue(0);
        builder.Property(s => s.GoalKicksAgainst).HasDefaultValue(0);

        builder.HasOne<Squad>().WithMany().HasForeignKey(s => s.SquadId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Match>().WithMany().HasForeignKey(s => s.MatchId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Coach>().WithMany().HasForeignKey(s => s.RecordedByCoachId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Club>().WithMany().HasForeignKey(s => s.RecordedByClubId).OnDelete(DeleteBehavior.Restrict);

        // Un único registro por plantilla y partido.
        builder.HasIndex(s => new { s.SquadId, s.MatchId }).IsUnique();
    }
}
