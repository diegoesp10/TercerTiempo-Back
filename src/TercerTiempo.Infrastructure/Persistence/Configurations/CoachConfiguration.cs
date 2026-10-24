using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Coaches;

namespace TercerTiempo.Infrastructure.Persistence.Configurations;

internal sealed class CoachConfiguration : IEntityTypeConfiguration<Coach>
{
    public void Configure(EntityTypeBuilder<Coach> builder)
    {
        builder.ToTable("Coaches");
        builder.ConfigureAggregate();

        builder.Property(c => c.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(c => c.NormalizedName).HasMaxLength(100).IsRequired();

        builder.HasIndex(c => c.UserId).IsUnique();
    }
}
