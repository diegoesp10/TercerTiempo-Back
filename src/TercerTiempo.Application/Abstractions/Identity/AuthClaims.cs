namespace TercerTiempo.Application.Abstractions.Identity;

/// <summary>Claims propios que viajan en el JWT además de los estándar (sub, email, jti).</summary>
public static class AuthClaims
{
    public const string AccountType = "account_type";
    public const string PlayerId = "player_id";
    public const string CoachId = "coach_id";
}
