using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TercerTiempo.Infrastructure.Persistence;
using TercerTiempo.Infrastructure.RealData;

namespace TercerTiempo.Application.Tests;

public class EfModelTests
{
    // Una columna con valor por defecto en SQL Server cuyo tipo tiene un valor vacío (el 0 de un enum) hace que EF avise al
    // arrancar cada proceso de que no distingue ese valor de «sin valor». No hace falta base: basta con montar el modelo.
    [Fact]
    public void The_models_have_no_ambiguous_database_defaults()
    {
        using var app = new AppDbContext(Options<AppDbContext>());
        using var realData = new RealDataDbContext(Options<RealDataDbContext>());

        Assert.NotNull(app.Model);
        Assert.NotNull(realData.Model);
    }

    // Lo que se cambie en la configuración de EF sin su migración haría fallar el arranque (Migrate lo trata como error).
    [Fact]
    public void The_models_match_their_last_migration()
    {
        using var app = new AppDbContext(Options<AppDbContext>());
        using var realData = new RealDataDbContext(Options<RealDataDbContext>());

        Assert.False(app.Database.HasPendingModelChanges());
        Assert.False(realData.Database.HasPendingModelChanges());
    }

    private static DbContextOptions<TContext> Options<TContext>() where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseSqlServer("Server=localhost;Database=modelo;Trusted_Connection=True")
            .ConfigureWarnings(w => w.Throw(RelationalEventId.BoolWithDefaultWarning))
            .Options;
}
