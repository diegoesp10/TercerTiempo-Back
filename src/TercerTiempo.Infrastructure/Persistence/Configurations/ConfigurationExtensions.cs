using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Common;

namespace TercerTiempo.Infrastructure.Persistence.Configurations;

internal static class ConfigurationExtensions
{
    public static void ConfigureAggregate<T>(this EntityTypeBuilder<T> builder) where T : AggregateRoot
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Ignore(e => e.DomainEvents);
    }

    /// <summary>Mapea el value object ExternalRef como columnas ExternalProvider/ExternalId de la propia tabla.</summary>
    public static void OwnsExternalRef<T>(this EntityTypeBuilder<T> builder, Expression<Func<T, ExternalRef?>> navigation)
        where T : class
    {
        builder.OwnsOne<ExternalRef>(navigation, (OwnedNavigationBuilder<T, ExternalRef> owned) =>
        {
            owned.Property(r => r.Provider).HasColumnName("ExternalProvider").HasMaxLength(50);
            owned.Property(r => r.ExternalId).HasColumnName("ExternalId").HasMaxLength(200);
            owned.HasIndex(r => new { r.Provider, r.ExternalId });
        });
    }
}
