using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Infrastructure.Persistence.Configurations;

/// <summary>
/// Tabla ErrorMessages con todos los mensajes del catálogo como datos de semilla: cada migración que cambie el
/// catálogo incluye las filas nuevas o modificadas, así que al crear la base de datos desde cero quedan insertados.
/// </summary>
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
