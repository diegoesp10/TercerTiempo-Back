using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Infrastructure.Persistence.Configurations;

internal sealed class BodySiteConfiguration : IEntityTypeConfiguration<BodySite>
{
    public void Configure(EntityTypeBuilder<BodySite> builder)
    {
        builder.ToTable("BodySites");
        builder.HasKey(s => s.Code);
        builder.Property(s => s.Code).HasMaxLength(BodySite.CodeMaxLength);
        builder.Property(s => s.Label).HasMaxLength(BodySite.LabelMaxLength).IsRequired();
        builder.HasIndex(s => s.Label).IsUnique();
        builder.Property(s => s.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Region).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Agreement).HasConversion<string>().HasMaxLength(20);
        // El grupo no se borra mientras tenga sitios dentro.
        builder.Property(s => s.ParentCode).HasMaxLength(BodySite.CodeMaxLength);
        builder.HasOne<BodySite>().WithMany().HasForeignKey(s => s.ParentCode).OnDelete(DeleteBehavior.NoAction);
    }
}
