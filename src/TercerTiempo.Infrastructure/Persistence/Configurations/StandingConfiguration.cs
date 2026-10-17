using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Standings;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Infrastructure.Persistence.Configurations;

internal sealed class StandingConfiguration : IEntityTypeConfiguration<Standing>
{
    public void Configure(EntityTypeBuilder<Standing> builder)
    {
        builder.ToTable("Standings");
        builder.ConfigureAggregate();
        builder.Ignore(s => s.GoalDifference);

        builder.HasOne<Season>().WithMany().HasForeignKey(s => s.SeasonId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Team>().WithMany().HasForeignKey(s => s.TeamId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => new { s.SeasonId, s.TeamId }).IsUnique();
    }
}
