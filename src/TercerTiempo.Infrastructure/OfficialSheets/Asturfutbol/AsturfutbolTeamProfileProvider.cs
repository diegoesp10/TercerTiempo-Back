using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Teams;

namespace TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;

// De asturfutbol.es solo se descargan actas: del equipo se ofrece el enlace a su ficha, sin pedirla.
internal sealed class AsturfutbolTeamProfileProvider(
    AsturfutbolClient client,
    IOptions<OfficialSheetOptions> options) : ITeamProfileProvider
{
    private readonly AsturfutbolOptions _settings = options.Value.Asturfutbol;

    public string ProviderName => AsturfutbolOfficialSheetProvider.Name;

    public string TeamUrl(string teamCode) =>
        client.AbsoluteUrl($"NFG_VisEquipos?cod_primaria={_settings.TeamsCode}&Codigo_Equipo={teamCode}");
}
