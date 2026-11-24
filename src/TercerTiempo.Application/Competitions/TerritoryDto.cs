using System.ComponentModel;
using TercerTiempo.Domain.Competitions;

namespace TercerTiempo.Application.Competitions;

[Description("Ámbito de una liga: quién la organiza, la federación española (Nacional) o la de una comunidad (Asturias…).")]
public sealed record TerritoryDto(Guid Id, string Name, TerritoryKind Kind)
{
    public static TerritoryDto From(Territory territory) => new(territory.Id, territory.Name, territory.Kind);

    // Por si una liga apunta a un ámbito que esta base aún no tiene (no debería pasar: se copian antes que las ligas).
    public static TerritoryDto Of(Guid territoryId, IReadOnlyDictionary<Guid, Territory> territories) =>
        territories.TryGetValue(territoryId, out var territory)
            ? From(territory)
            : territoryId == Territory.NationalId
                ? new(territoryId, Territory.NationalName, TerritoryKind.National)
                : new(territoryId, Territory.AsturiasName, TerritoryKind.Regional);
}
