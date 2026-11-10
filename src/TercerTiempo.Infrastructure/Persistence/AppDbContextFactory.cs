using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TercerTiempo.Infrastructure.Persistence;

// Permite a dotnet-ef (migrations add / database update) trabajar con Infrastructure como proyecto de arranque, sin
// compilar la API; útil cuando Visual Studio la tiene en ejecución y bloquea su bin. La cadena sale de
// ConnectionStrings__Default o, si no está, de la base de desarrollo local.
internal sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string DevelopmentConnection = "Server=localhost;Database=TercerTiempo;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? DevelopmentConnection;
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;
        return new AppDbContext(options);
    }
}
