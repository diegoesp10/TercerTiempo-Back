using TercerTiempo.Application.Seasons;

namespace TercerTiempo.Api.Infrastructure;

// La temporada que elige el frontal viaja en la cabecera X-Season (o ?season= en enlaces directos).
internal sealed class SeasonRequest(IHttpContextAccessor httpContextAccessor) : ISeasonRequest
{
    public const string Header = "X-Season";

    public string? Requested
    {
        get
        {
            var request = httpContextAccessor.HttpContext?.Request;
            if (request is null) return null;
            var header = request.Headers[Header].FirstOrDefault();
            return string.IsNullOrWhiteSpace(header) ? request.Query["season"].FirstOrDefault() : header;
        }
    }
}
