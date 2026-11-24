using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Clubs;
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
        builder.Ignore(i => i.ExpectedDays);
        builder.Ignore(i => i.InitialExpectedDays);
        builder.Ignore(i => i.ContinuedOn);

        // El tipo, del catálogo que mantiene el administrador: viene siempre con la lesión y no se borra mientras alguna
        // lo tenga.
        builder.Property(i => i.Type).HasMaxLength(InjuryTypeDefinition.CodeMaxLength);
        builder.HasOne(i => i.TypeDefinition).WithMany().HasForeignKey(i => i.Type).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(i => i.TypeDefinition).AutoInclude();
        builder.Property(i => i.Reason).HasMaxLength(300);

        builder.HasOne<Squad>().WithMany().HasForeignKey(i => i.SquadId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SquadPlayer>().WithMany().HasForeignKey(i => i.SquadPlayerId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Coach>().WithMany().HasForeignKey(i => i.RecordedByCoachId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Club>().WithMany().HasForeignKey(i => i.RecordedByClubId).OnDelete(DeleteBehavior.Restrict);
        // La recaída apunta a la lesión superada; al borrar esa (registrada por error) se le quita antes el enlace.
        builder.HasOne<PlayerInjury>().WithMany().HasForeignKey(i => i.RelapseOfInjuryId).OnDelete(DeleteBehavior.NoAction);
        // La continuación en la temporada siguiente apunta a la de la anterior (una como mucho); al borrar aquella se le
        // quita antes el enlace.
        builder.HasOne<PlayerInjury>().WithMany().HasForeignKey(i => i.ContinuesInjuryId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(i => i.ContinuesInjuryId).IsUnique().HasFilter("[ContinuesInjuryId] IS NOT NULL");

        builder.HasIndex(i => new { i.SquadId, i.SquadPlayerId });
    }
}
