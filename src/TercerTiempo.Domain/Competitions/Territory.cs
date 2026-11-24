using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Competitions;

// Ámbito de una liga: quién la organiza, la federación española (Nacional) o la de una comunidad autónoma. Las ligas se
// eligen primero por ámbito y después por liga. Nacional y Asturias vienen de serie (con estos Id en todas las bases);
// cada comunidad nueva se crea sola la primera vez que se sincroniza una liga suya.
public sealed class Territory : AggregateRoot
{
    public static readonly Guid NationalId = new("0b6f2a1e-7c34-4d8a-9f51-2e7d3c9a1b01");
    public static readonly Guid AsturiasId = new("5d9e8c47-1a2b-4f6e-8c3d-7b4a2e1f9c33");

    public const string NationalName = "Nacional";
    public const string AsturiasName = "Asturias";

    public string Name { get; private set; } = string.Empty;

    public TerritoryKind Kind { get; private set; }

    private Territory() { }

    public static Territory CreateRegion(string name) =>
        new() { Id = NewId(), Name = Guard.NotEmpty(name, "ámbito", 100), Kind = TerritoryKind.Regional };

    // Las profesionales, las de la RFEF y sus copas (y la del Rey) son del ámbito nacional; las demás, del de su comunidad.
    public static bool IsNational(CompetitionLevel level, string category) =>
        level is CompetitionLevel.Professional or CompetitionLevel.National
        || (level == CompetitionLevel.Cup
            && (category.Contains("RFEF", StringComparison.OrdinalIgnoreCase) || category.Contains("del Rey", StringComparison.OrdinalIgnoreCase)));
}

public enum TerritoryKind
{
    National = 1,
    Regional = 2,
}

public interface ITerritoryStore
{
    Task<IReadOnlyList<Territory>> GetAllAsync(CancellationToken cancellationToken = default);

    // También las que se acaban de añadir y aún no se han guardado: una sincronización puede traer varias ligas de una
    // comunidad nueva.
    Task<Territory?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    void Add(Territory territory);
}
