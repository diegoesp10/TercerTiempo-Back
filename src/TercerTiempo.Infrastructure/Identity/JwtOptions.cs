namespace TercerTiempo.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "TercerTiempo";
    public string Audience { get; set; } = "TercerTiempo.Web";

    /// <summary>Clave simétrica HMAC-SHA256. Mínimo 32 caracteres. Nunca en appsettings de producción.</summary>
    public string Key { get; set; } = string.Empty;

    public int ExpirationMinutes { get; set; } = 720;
}
