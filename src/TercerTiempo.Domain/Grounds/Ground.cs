using System.Globalization;
using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Grounds;

// Un campo de fútbol de las ligas, tal como lo nombra el proveedor de datos en sus partidos («La Pixarra», «Hermanos
// Llana»…). El proveedor solo da el nombre: la dirección y el punto exacto se buscan aparte en OpenStreetMap y, si no se
// encuentra un campo o recinto deportivo con ese nombre en Asturias, se queda sin ubicar (el mapa se abre buscando por
// el nombre). Es información pública y va en la base de datos reales que comparten los entornos.
public sealed class Ground : AggregateRoot
{
    public const int MaxNameLength = 150;
    public const int MaxAddressLength = 300;
    public const int MaxLocalityLength = 120;
    public const int MaxLocateAttempts = 3;
    public static readonly TimeSpan RetryLocateAfter = TimeSpan.FromDays(30);

    public string Name { get; private set; } = string.Empty;
    // El nombre sin tildes ni mayúsculas, para buscar.
    public string Key { get; private set; } = string.Empty;
    public string? Address { get; private set; }
    public string? Locality { get; private set; }
    public decimal? Latitude { get; private set; }
    public decimal? Longitude { get; private set; }
    // Última vez que se buscó su ubicación (con o sin suerte) y cuántas veces se ha buscado sin encontrarla.
    public DateTimeOffset? LocatedAt { get; private set; }
    public int LocateAttempts { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsLocated => Latitude is not null && Longitude is not null;

    private Ground() { }

    public static Ground Create(string name, DateTimeOffset now)
    {
        var clean = Guard.NotEmpty(name?.Trim() ?? string.Empty, "campo", MaxNameLength);
        return new Ground { Id = NewId(), Name = clean, Key = KeyOf(clean), CreatedAt = now };
    }

    public static string KeyOf(string? name) => NameNormalizer.Normalize(name);

    // Toca buscarla si nunca se ha encontrado, quedan intentos y ha pasado el plazo desde el último.
    public bool NeedsLocating(DateTimeOffset now) =>
        !IsLocated && LocateAttempts < MaxLocateAttempts && (LocatedAt is not { } last || now - last >= RetryLocateAfter);

    public void Locate(GroundLocation? found, DateTimeOffset now)
    {
        LocatedAt = now;
        if (found is null)
        {
            LocateAttempts++;
            return;
        }

        Guard.InRange(found.Latitude, -90m, 90m, "latitud");
        Guard.InRange(found.Longitude, -180m, 180m, "longitud");
        Latitude = Math.Round(found.Latitude, 6);
        Longitude = Math.Round(found.Longitude, 6);
        Address = Trim(found.Address, MaxAddressLength);
        Locality = Trim(found.Locality, MaxLocalityLength);
    }

    // Enlace a Google Maps: al punto exacto si está ubicado; si no, la búsqueda del campo por su nombre en Asturias.
    public string MapsUrl
    {
        get
        {
            var query = IsLocated
                ? $"{Latitude!.Value.ToString(CultureInfo.InvariantCulture)},{Longitude!.Value.ToString(CultureInfo.InvariantCulture)}"
                : $"Campo de fútbol {Name}, {Locality ?? "Asturias"}";
            return $"https://www.google.com/maps/search/?api=1&query={Uri.EscapeDataString(query)}";
        }
    }

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var clean = value.Trim();
        return clean.Length <= max ? clean : clean[..max].TrimEnd();
    }
}

public sealed record GroundLocation(string? Address, string? Locality, decimal Latitude, decimal Longitude);

// Un campo usado en los partidos de una temporada: cuántos partidos ha jugado allí cada equipo como local.
public sealed record SeasonGround(Ground Ground, IReadOnlyDictionary<Guid, int> HomeMatchesByTeam)
{
    public int Matches => HomeMatchesByTeam.Values.Sum();
}

public interface IGroundStore
{
    Task<Ground?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Ground>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task<Ground?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    // Los campos de los partidos de la temporada (por el nombre que trae cada partido), con sus locales.
    Task<IReadOnlyList<SeasonGround>> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default);
    // Nombres de campo de los partidos que aún no están en el catálogo.
    Task<IReadOnlyList<string>> GetUncataloguedVenuesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Ground>> GetToLocateAsync(DateTimeOffset now, int take, CancellationToken cancellationToken = default);
    void Add(Ground ground);
}
