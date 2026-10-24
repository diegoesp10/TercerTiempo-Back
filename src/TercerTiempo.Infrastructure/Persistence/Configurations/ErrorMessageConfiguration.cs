using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Infrastructure.Persistence.Configurations;

internal sealed class ErrorMessageConfiguration : IEntityTypeConfiguration<ErrorMessage>
{
    public void Configure(EntityTypeBuilder<ErrorMessage> builder)
    {
        builder.ToTable("ErrorMessages");
        builder.HasKey(e => new { e.Code, e.Language });

        builder.Property(e => e.Code).HasMaxLength(100);
        builder.Property(e => e.Language).HasMaxLength(10);
        builder.Property(e => e.Message).HasMaxLength(500).IsRequired();

        builder.HasData(ErrorCatalog.Messages);
    }
}
