using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Teams;

/// <summary>
/// URL del escudo que se entrega a los clientes: siempre el guardado por nosotros y servido por la API (nunca una
/// imagen enlazada a terceros). Lleva la versión del archivo en la query para que el cambio de escudo invalide la caché.
/// </summary>
public static class TeamCrests
{
    /// <summary>Ruta relativa a la API por la que se sirve el escudo guardado.</summary>
    public static string Route(Guid teamId, string? crestKey = null) =>
        crestKey is null ? $"/api/teams/{teamId}/crest" : $"/api/teams/{teamId}/crest?v={Version(crestKey)}";

    public static string? CrestLink(this Team team) => team.CrestKey is null ? null : Route(team.Id, team.CrestKey);

    /// <summary>Parte variable de la clave ("{teamId}-{versión}.{ext}" → versión).</summary>
    private static string Version(string crestKey)
    {
        var name = Path.GetFileNameWithoutExtension(crestKey);
        var dash = name.LastIndexOf('-');
        return dash >= 0 ? name[(dash + 1)..] : name;
    }
}
