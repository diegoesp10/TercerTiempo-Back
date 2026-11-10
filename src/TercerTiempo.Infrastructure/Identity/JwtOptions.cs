namespace TercerTiempo.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "TercerTiempo";
    public string Audience { get; set; } = "TercerTiempo.Web";

    public string Key { get; set; } = string.Empty;

    public int ExpirationMinutes { get; set; } = 720;
}
