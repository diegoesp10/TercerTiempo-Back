using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using FluentValidation.Results;
using TercerTiempo.Application.Clubs;
using TercerTiempo.Application.Clubs.Catalog;
using TercerTiempo.Application.Clubs.Invitations;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Coaches;
using TercerTiempo.Application.Coaches.Appointments;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Players;
using TercerTiempo.Application.Players.Invites;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Auth.Register;

public sealed record RegisterCommand(
    [property: Description("Email de acceso. Debe ser único.")]
    string Email,
    [property: Description("Mínimo 8 caracteres y al menos un número.")]
    string Password,
    [property: Description("Cuerpo técnico: nombre y apellidos. Club: nombre tal y como figura en GET /api/clubs/catalog. Jugadores: se ignora, el nombre es el de la ficha del código.")]
    string? DisplayName,
    [property: Description("Player (jugador), Coach (cuerpo técnico) o Club.")]
    AccountType AccountType,
    [property: Description("Solo jugadores, opcional: Goalkeeper, Defender, Midfielder o Forward. Si falta se toma la posición de la ficha.")]
    Position? Position,
    [property: Description("Opcional, formato yyyy-MM-dd. Jugadores: mínimo 14 años.")]
    DateOnly? BirthDate,
    [property: Description("Cuerpo técnico: código de acceso que te ha entregado tu club. Jugadores: código personal de tu ficha, generado por tu club o entrenador; al crear la cuenta quedas vinculado a ella. Club: código de invitación a la beta (obligatorio si GET /api/auth/registration-options lo indica).")]
    string? AccessCode) : ICommand<AuthResponse>;

internal sealed class RegisterValidator : AbstractValidator<RegisterCommand>
{
    public RegisterValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).WithCatalogMessage(ErrorCode.AuthPasswordTooShort, 8).MaximumLength(100);
        RuleFor(x => x.DisplayName).NotEmpty().When(x => x.AccountType != AccountType.Player);
        RuleFor(x => x.DisplayName).MaximumLength(120);
        RuleFor(x => x.AccountType).IsInEnum();
        RuleFor(x => x.Position).IsInEnum().When(x => x.Position.HasValue);
        RuleFor(x => x.AccessCode).NotEmpty().When(x => x.AccountType == AccountType.Coach)
            .WithCatalogMessage(ErrorCode.AuthAccessCodeRequired);
        RuleFor(x => x.AccessCode).NotEmpty().When(x => x.AccountType == AccountType.Player)
            .WithCatalogMessage(ErrorCode.AuthInviteCodeRequired);
        RuleFor(x => x.AccessCode).MaximumLength(20);
    }
}

internal sealed class RegisterHandler(
    IIdentityService identity,
    AccountStores stores,
    AppointmentRedemption redemption,
    IClubInvitationStore clubInvitations,
    IOptions<RegistrationOptions> registration,
    IUnitOfWork unitOfWork,
    ITokenService tokens) : ICommandHandler<RegisterCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(RegisterCommand command, CancellationToken cancellationToken)
    {
        var email = command.Email.Trim();
        var now = stores.Clock.GetUtcNow();

        // El código y el club se comprueban antes de crear el usuario para no dejar cuentas huérfanas si no valen.
        CoachAppointment? appointment = null;
        if (command.AccountType == AccountType.Coach)
            appointment = await redemption.FindRedeemableAsync(command.AccessCode!, cancellationToken);

        InviteMatch? invite = null;
        if (command.AccountType == AccountType.Player)
            invite = await InviteLookup.FindAsync(command.AccessCode!, stores.Squads, cancellationToken);

        ClubCatalogEntry? clubEntry = null;
        ClubInvitation? clubInvitation = null;
        if (command.AccountType == AccountType.Club)
        {
            if (registration.Value.ClubInviteRequired)
                clubInvitation = await FindClubInvitationAsync(command.AccessCode, now, cancellationToken);
            clubEntry = await FindClubAsync(command.DisplayName!, cancellationToken);
        }

        var userId = await identity.CreateUserAsync(email, command.Password, cancellationToken);

        Guid profileId;

        switch (command.AccountType)
        {
            case AccountType.Player:
                var (squad, entry) = invite!;
                var player = Player.Register(userId, entry.FullName, command.Position ?? entry.Position ?? Position.Midfielder, command.BirthDate, now);
                stores.Players.Add(player);
                squad.LinkPlayer(entry.Id, player.Id);
                profileId = player.Id;
                break;

            case AccountType.Coach:
                var coach = Coach.Register(userId, command.DisplayName!, now);
                stores.Coaches.Add(coach);
                await redemption.RedeemAsync(appointment!, coach, now, cancellationToken);
                profileId = coach.Id;
                break;

            default:
                var club = Club.Register(userId, clubEntry!.Name, now);
                foreach (var team in clubEntry.Teams)
                {
                    if (await stores.Clubs.GetByTeamAsync(team.Team.Id, cancellationToken) is null)
                        club.AddTeam(team.Team.Id, now);
                }
                stores.Clubs.Add(club);
                clubInvitation?.Redeem(club.Id, now);
                profileId = club.Id;
                break;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var token = tokens.CreateToken(userId, email, command.AccountType, profileId);
        var account = await AccountResolver.ForUserAsync(userId, stores, cancellationToken);
        return new AuthResponse(token.AccessToken, token.ExpiresAt, account);
    }

    private async Task<ClubInvitation> FindClubInvitationAsync(string? code, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw InvalidAccessCode(ErrorCode.AuthClubInviteRequired);

        var invitation = await clubInvitations.GetByCodeAsync(InviteCodes.Normalize(code), cancellationToken);
        return invitation is not null && invitation.IsUsable(now) ? invitation : throw InvalidAccessCode(ErrorCode.AuthClubInviteInvalid);
    }

    private static ValidationException InvalidAccessCode(string code) =>
        new([new ValidationFailure(nameof(RegisterCommand.AccessCode), ErrorCatalog.Template(code)) { ErrorCode = code }]);

    private async Task<ClubCatalogEntry> FindClubAsync(string name, CancellationToken cancellationToken)
    {
        var normalized = NameNormalizer.Normalize(name);
        var catalog = await ClubCatalog.LoadAsync(stores, cancellationToken);
        var entry = catalog.FirstOrDefault(e => e.NormalizedName == normalized)
            ?? throw new ValidationException([new ValidationFailure(nameof(RegisterCommand.DisplayName), ErrorCatalog.Template(ErrorCode.AuthClubNotInCatalog)) { ErrorCode = ErrorCode.AuthClubNotInCatalog }]);

        if (await stores.Clubs.GetByNormalizedNameAsync(entry.NormalizedName, cancellationToken) is not null)
            throw new ConflictException(ErrorCode.AuthClubAlreadyRegistered, entry.Name);

        return entry;
    }
}
