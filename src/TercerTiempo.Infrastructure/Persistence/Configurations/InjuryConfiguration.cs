using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Infrastructure.Persistence.Configurations;

internal sealed class InjuryConfiguration : IEntityTypeConfiguration<PlayerInjury>
{
    public void Configure(EntityTypeBuilder<PlayerInjury> builder)
    {
        builder.ToTable("PlayerInjuries");
        builder.ConfigureAggregate();
        builder.Ignore(i => i.IsActive);

        builder.Property(i => i.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(i => i.Reason).HasMaxLength(300);

        builder.HasOne<Squad>().WithMany().HasForeignKey(i => i.SquadId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SquadPlayer>().WithMany().HasForeignKey(i => i.SquadPlayerId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Coach>().WithMany().HasForeignKey(i => i.RecordedByCoachId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => new { i.SquadId, i.SquadPlayerId });
    }
}
