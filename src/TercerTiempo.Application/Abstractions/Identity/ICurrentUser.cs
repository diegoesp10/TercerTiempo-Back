namespace TercerTiempo.Application.Abstractions.Identity;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    AccountType? AccountType { get; }

    Guid? PlayerId { get; }

    Guid? CoachId { get; }

    Guid? ClubId { get; }
}
