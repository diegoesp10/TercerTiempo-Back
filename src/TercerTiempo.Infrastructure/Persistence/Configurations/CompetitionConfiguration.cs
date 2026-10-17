using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Competitions;

namespace TercerTiempo.Infrastructure.Persistence.Configurations;

internal sealed class CompetitionConfiguration : IEntityTypeConfiguration<Competition>
{
    public void Configure(EntityTypeBuilder<Competition> builder)
    {
        builder.ToTable("Competitions");
        builder.ConfigureAggregate();

        builder.Property(c => c.Name).HasMaxLength(150).IsRequired();
        builder.Property(c => c.Region).HasMaxLength(100).IsRequired();

        builder.OwnsExternalRef(c => c.ExternalRef);

        builder.HasMany(c => c.Seasons)
            .WithOne()
            .HasForeignKey(s => s.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(c => c.Seasons)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();
    }
}

internal sealed class SeasonConfiguration : IEntityTypeConfiguration<Season>
{
    public void Configure(EntityTypeBuilder<Season> builder)
    {
        builder.ToTable("Seasons");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.Label).HasMaxLength(20).IsRequired();

        builder.HasIndex(s => new { s.CompetitionId, s.Label }).IsUnique();
        builder.HasIndex(s => s.IsCurrent);

        builder.OwnsExternalRef(s => s.ExternalRef);
    }
}
