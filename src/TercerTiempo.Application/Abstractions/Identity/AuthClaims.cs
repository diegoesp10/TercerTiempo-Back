namespace TercerTiempo.Application.Abstractions.Identity;

/// <summary>Claims propios que viajan en el JWT además de los estándar (sub, email, jti).</summary>
public static class AuthClaims
{
    public const string AccountType = "account_type";
    public const string PlayerId = "player_id";
    public const string CoachId = "coach_id";
    public const string ClubId = "club_id";

    /// <summary>Nombre del claim que lleva el id del perfil según el tipo de cuenta.</summary>
    public static string ProfileClaimFor(Identity.AccountType accountType) => accountType switch
    {
        Identity.AccountType.Player => PlayerId,
        Identity.AccountType.Coach => CoachId,
        Identity.AccountType.Club => ClubId,
        _ => throw new ArgumentOutOfRangeException(nameof(accountType)),
    };
}
