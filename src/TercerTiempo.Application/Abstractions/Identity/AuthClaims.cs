namespace TercerTiempo.Application.Abstractions.Identity;

public static class AuthClaims
{
    public const string AccountType = "account_type";
    public const string PlayerId = "player_id";
    public const string CoachId = "coach_id";
    public const string ClubId = "club_id";
    public const string Admin = "admin";
    public const string AdminPolicy = "Admin";
    public const string AdminRole = "Admin";

    public static string ProfileClaimFor(Identity.AccountType accountType) => accountType switch
    {
        Identity.AccountType.Player => PlayerId,
        Identity.AccountType.Coach => CoachId,
        Identity.AccountType.Club => ClubId,
        _ => throw new ArgumentOutOfRangeException(nameof(accountType)),
    };
}
