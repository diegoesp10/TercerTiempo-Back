using Microsoft.EntityFrameworkCore;
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
    }
}
