using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Infrastructure.Persistence.Configurations;

internal sealed class InjuryTypeConfiguration : IEntityTypeConfiguration<InjuryTypeDefinition>
{
    public void Configure(EntityTypeBuilder<InjuryTypeDefinition> builder)
    {
        builder.ToTable("InjuryTypes");
        builder.HasKey(t => t.Code);
        builder.Property(t => t.Code).HasMaxLength(InjuryTypeDefinition.CodeMaxLength);
        builder.Property(t => t.Label).HasMaxLength(InjuryTypeDefinition.LabelMaxLength).IsRequired();
        builder.HasIndex(t => t.Label).IsUnique();

        // Lo que pregunta: el sitio fijo viene siempre con el tipo (de él sale el lado); la lista son códigos de sitios,
        // separados por comas, que se comprueban al guardar el tipo y al borrar un sitio.
        builder.Property(t => t.SiteKind).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.BaseSiteCode).HasMaxLength(BodySite.CodeMaxLength);
        builder.HasOne(t => t.BaseSite).WithMany().HasForeignKey(t => t.BaseSiteCode).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(t => t.BaseSite).AutoInclude();
        builder.Property(t => t.SiteCodes)
            .HasConversion(
                codes => string.Join(',', codes),
                text => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                new ValueComparer<IReadOnlyList<string>>(
                    (a, b) => a!.SequenceEqual(b!),
                    codes => codes.Aggregate(0, (hash, code) => HashCode.Combine(hash, code.GetHashCode(StringComparison.Ordinal))),
                    codes => codes.ToList()))
            .HasMaxLength(2500)
            .IsRequired();
    }
}
