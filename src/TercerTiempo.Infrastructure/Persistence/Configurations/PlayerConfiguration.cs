using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Players;

namespace TercerTiempo.Infrastructure.Persistence.Configurations;

internal sealed class PlayerConfiguration : IEntityTypeConfiguration<Player>
{
    public void Configure(EntityTypeBuilder<Player> builder)
    {
        builder.ToTable("Players");
        builder.ConfigureAggregate();

        builder.Property(p => p.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.NormalizedName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Position).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(p => p.UserId).IsUnique();
        builder.HasIndex(p => p.NormalizedName);
    }
}
