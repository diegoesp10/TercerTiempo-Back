using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using FluentValidation.Results;
using TercerTiempo.Application.Clubs;
using TercerTiempo.Application.Clubs.Catalog;
using TercerTiempo.Application.Coaches;
using TercerTiempo.Application.Coaches.Appointments;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Players;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Players;

namespace TercerTiempo.Application.Auth.Register;

/// <summary>
/// Alta en la plataforma como jugador, como técnico o como club. Un técnico solo puede darse de alta con el código de
/// acceso que le entrega su club: al crear la cuenta se canjea y queda en el cuerpo técnico de la plantilla. Un club
/// elige su nombre del catálogo (GET /api/clubs/catalog): solo puede haber una cuenta por club y al crearla quedan
/// vinculados todos sus equipos.
/// </summary>
public sealed record RegisterCommand(
    [property: Description("Email de acceso. Debe ser único.")]
    string Email,
    [property: Description("Mínimo 8 caracteres y al menos un número.")]
    string Password,
    [property: Description("Nombre y apellidos (jugadores: tal y como aparecen en la plantilla de su equipo) o nombre del club, tal y como figura en GET /api/clubs/catalog.")]
    string DisplayName,
    [property: Description("Player (jugador), Coach (cuerpo técnico) o Club.")]
    AccountType AccountType,
    [property: Description("Solo jugadores: Goalkeeper, Defender, Midfielder o Forward.")]
    Position? Position,
    [property: Description("Opcional, formato yyyy-MM-dd. Jugadores: mínimo 14 años.")]
    DateOnly? BirthDate,
    [property: Description("Solo cuerpo técnico: código de acceso que te ha entregado tu club.")]
    string? AccessCode) : ICommand<AuthResponse>;

internal sealed class RegisterValidator : AbstractValidator<RegisterCommand>
{
    public RegisterValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(100);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.AccountType).IsInEnum();
        RuleFor(x => x.Position).NotNull().When(x => x.AccountType == AccountType.Player)
            .WithMessage("Indica tu posición.");
        RuleFor(x => x.Position).IsInEnum().When(x => x.Position.HasValue);
        RuleFor(x => x.AccessCode).NotEmpty().When(x => x.AccountType == AccountType.Coach)
            .WithMessage("Introduce el código de acceso que te ha dado tu club.");
        RuleFor(x => x.AccessCode).MaximumLength(20);
    }
}

internal sealed class RegisterHandler(
    IIdentityService identity,
    AccountStores stores,
    AppointmentRedemption redemption,
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

        ClubCatalogEntry? clubEntry = null;
        if (command.AccountType == AccountType.Club)
            clubEntry = await FindClubAsync(command.DisplayName, cancellationToken);

        var userId = await identity.CreateUserAsync(email, command.Password, cancellationToken);

        Guid profileId;

        switch (command.AccountType)
        {
            case AccountType.Player:
                var player = Player.Register(userId, command.DisplayName, command.Position!.Value, command.BirthDate, now);
                stores.Players.Add(player);
                profileId = player.Id;
                break;

            case AccountType.Coach:
                var coach = Coach.Register(userId, command.DisplayName, now);
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
                profileId = club.Id;
                break;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var token = tokens.CreateToken(userId, email, command.AccountType, profileId);
        var account = await AccountResolver.ForUserAsync(userId, stores, cancellationToken);
        return new AuthResponse(token.AccessToken, token.ExpiresAt, account);
    }

    /// <summary>El club tiene que estar en el catálogo del proveedor y no tener ya cuenta.</summary>
    private async Task<ClubCatalogEntry> FindClubAsync(string name, CancellationToken cancellationToken)
    {
        var normalized = NameNormalizer.Normalize(name);
        var catalog = await ClubCatalog.LoadAsync(stores, cancellationToken);
        var entry = catalog.FirstOrDefault(e => e.NormalizedName == normalized)
            ?? throw new ValidationException([new ValidationFailure(nameof(RegisterCommand.DisplayName), "Elige tu club de la lista: solo pueden darse de alta los clubes de las ligas que sigue Tercer Tiempo.")]);

        if (await stores.Clubs.GetByNormalizedNameAsync(entry.NormalizedName, cancellationToken) is not null)
            throw new ConflictException($"{entry.Name} ya tiene cuenta en Tercer Tiempo.");

        return entry;
    }
}
