using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Teams;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;

internal sealed class AsturfutbolTeamProfileProvider(
    AsturfutbolClient client,
    IOptions<OfficialSheetOptions> options,
    ILogger<AsturfutbolTeamProfileProvider> logger) : ITeamProfileProvider
{
    private readonly AsturfutbolOptions _settings = options.Value.Asturfutbol;

    public string ProviderName => AsturfutbolOfficialSheetProvider.Name;

    public string TeamUrl(string teamCode) =>
        client.AbsoluteUrl($"NFG_VisEquipos?cod_primaria={_settings.TeamsCode}&Codigo_Equipo={teamCode}");

    public async Task<OfficialTeamProfile?> GetProfileAsync(string teamCode, CancellationToken cancellationToken)
    {
        var html = await client.GetHtmlAsync($"NFG_VisEquipos?cod_primaria={_settings.TeamsCode}&Codigo_Equipo={teamCode}", cancellationToken);
        if (html is null || AsturfutbolPages.ParseTeamProfile(html) is not var (profile, groundCode))
            return null;

        if (groundCode is null)
            return profile;

        // La página del campo completa la dirección; si falla, la ficha vale igual.
        try
        {
            var groundHtml = await client.GetHtmlAsync($"NFG_VisCampos?cod_primaria={_settings.GroundsCode}&Codigo_Campo={groundCode}", cancellationToken);
            if (groundHtml is not null && AsturfutbolPages.ParseGround(groundHtml) is { } ground)
            {
                return profile with
                {
                    Profile = new TeamProfile
                    {
                        OfficialName = profile.Profile.OfficialName,
                        Locality = profile.Profile.Locality,
                        Province = profile.Profile.Province,
                        Address = profile.Profile.Address,
                        PostalCode = profile.Profile.PostalCode,
                        Website = profile.Profile.Website,
                        Ground = profile.Profile.Ground,
                        GroundSurface = ground.Surface ?? profile.Profile.GroundSurface,
                        GroundAddress = ground.Address,
                        GroundLocality = ground.Locality,
                        ShirtColor = profile.Profile.ShirtColor,
                        ShortsColor = profile.Profile.ShortsColor,
                        SocksColor = profile.Profile.SocksColor,
                    },
                };
            }
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "asturfutbol.es: no se pudo leer el campo {Ground} del equipo {Team}", groundCode, teamCode);
        }

        return profile;
    }

    public async Task<CrestImage?> DownloadCrestAsync(string crestUrl, CancellationToken cancellationToken)
    {
        var image = await client.GetBytesAsync(crestUrl, cancellationToken);
        if (image is null) return null;

        var (content, contentType) = image.Value;
        if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) || content.Length == 0)
        {
            logger.LogWarning("asturfutbol.es: el escudo {Url} no es una imagen ({ContentType})", crestUrl, contentType);
            return null;
        }

        return new CrestImage(content, contentType.ToLowerInvariant());
    }
}
