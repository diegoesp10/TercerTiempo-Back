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

    public static void OwnsExternalRef<T>(this EntityTypeBuilder<T> builder, Expression<Func<T, ExternalRef?>> navigation)
        where T : class =>
        builder.OwnsExternalRef(navigation, "External");

    public static void OwnsExternalRef<T>(this EntityTypeBuilder<T> builder, Expression<Func<T, ExternalRef?>> navigation, string prefix)
        where T : class
    {
        builder.OwnsOne<ExternalRef>(navigation, (OwnedNavigationBuilder<T, ExternalRef> owned) =>
        {
            owned.Property(r => r.Provider).HasColumnName($"{prefix}Provider").HasMaxLength(50);
            owned.Property(r => r.ExternalId).HasColumnName($"{prefix}Id").HasMaxLength(200);
            owned.HasIndex(r => new { r.Provider, r.ExternalId });
        });
    }
}
