using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TercerTiempo.Infrastructure.RealData;

// Modelo de la base de datos reales: las mismas configuraciones que la base de cada entorno, solo con las tablas de
// RealDataModel. Sirve para sus migraciones (Persistence/RealDataMigrations); en ejecución las descargas usan el
// AppDbContext de siempre apuntando a esta base (RealDataAmbient).
public sealed class RealDataDbContext(DbContextOptions<RealDataDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder) =>
        builder.ApplyConfigurationsFromAssembly(typeof(RealDataDbContext).Assembly, ConfiguresRealData);

    private static bool ConfiguresRealData(Type configuration) =>
        configuration.GetInterfaces().Any(i => i.IsGenericType
            && i.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>)
            && RealDataModel.Stored.Contains(i.GetGenericArguments()[0]));
}

internal sealed class RealDataDbContextFactory : IDesignTimeDbContextFactory<RealDataDbContext>
{
    private const string DevelopmentConnection = "Server=localhost;Database=tercertiempo_DatosReales;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True";

    public RealDataDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__RealData") ?? DevelopmentConnection;
        var options = new DbContextOptionsBuilder<RealDataDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(RealDataDbContext).Assembly.FullName))
            .Options;
        return new RealDataDbContext(options);
    }
}

// Escudos descargados de las webs. Cada entorno los copia a sus ficheros con la misma clave (la del fichero).
internal sealed class TeamCrestImage
{
    public string Key { get; set; } = "";
    public Guid TeamId { get; set; }
    public string ContentType { get; set; } = "";
    public byte[] Content { get; set; } = [];
    public DateTimeOffset SavedAt { get; set; }
}

// Quién descarga ahora los datos reales: una sola fila que mantiene al día el entorno que tiene el bloqueo.
internal sealed class RealDataIngester
{
    public const int SingletonId = 1;

    public int Id { get; set; }
    public string Environment { get; set; } = "";
    public string Machine { get; set; } = "";
    public DateTimeOffset Since { get; set; }
    public DateTimeOffset HeartbeatAt { get; set; }
}

internal sealed class TeamCrestImageConfiguration : IEntityTypeConfiguration<TeamCrestImage>
{
    public void Configure(EntityTypeBuilder<TeamCrestImage> b)
    {
        b.ToTable("TeamCrestImages");
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasMaxLength(100);
        b.Property(x => x.ContentType).HasMaxLength(50).IsRequired();
        b.Property(x => x.Content).IsRequired();
        b.HasIndex(x => x.TeamId);
    }
}

internal sealed class RealDataIngesterConfiguration : IEntityTypeConfiguration<RealDataIngester>
{
    public void Configure(EntityTypeBuilder<RealDataIngester> b)
    {
        b.ToTable("RealDataIngester");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Environment).HasMaxLength(40).IsRequired();
        b.Property(x => x.Machine).HasMaxLength(100).IsRequired();
    }
}
