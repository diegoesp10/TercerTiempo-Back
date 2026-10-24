using TercerTiempo.Application.Auth.Login;
using TercerTiempo.Application.Auth.Register;
using TercerTiempo.Repository.Auth;

namespace TercerTiempo.Api.Endpoints;

internal static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapGet("/invite/{inviteCode}", (string inviteCode, IAuthRepository auth, CancellationToken ct) => auth.GetInviteAsync(inviteCode, ct))
            .WithName("GetInvite")
            .WithSummary("Consultar un código personal antes del alta")
            .WithDescription("Sin sesión. Devuelve la ficha (nombre, dorsal, posición) y la plantilla que hay detrás de un código personal vigente, para que el jugador confirme que es la suya antes de crear la cuenta. Nunca devuelve otras fichas.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/registration-options", (IAuthRepository auth, CancellationToken ct) => auth.GetRegistrationOptionsAsync(ct))
            .WithName("GetRegistrationOptions")
            .WithSummary("Condiciones del alta")
            .WithDescription("Sin sesión. clubInviteRequired = true durante la beta cerrada: el alta de un club exige en accessCode una invitación generada por el administrador.");

        group.MapPost("/register", (RegisterCommand command, IAuthRepository auth, CancellationToken ct) => auth.RegisterAsync(command, ct))
            .WithName("Register")
            .WithSummary("Alta en la plataforma")
            .WithDescription("Crea el usuario y su perfil según accountType y devuelve el JWT ya iniciado. Player: solo necesita el accessCode personal que le ha entregado su club o entrenador; el nombre es el de la ficha y la cuenta queda vinculada a ella al crearse (posición y fecha de nacimiento opcionales). Coach: necesita el accessCode que le ha entregado su club; al crear la cuenta se canjea y entra en el cuerpo técnico de la plantilla del equipo. Club: el nombre del club tal y como figura en GET /api/clubs/catalog; solo puede haber una cuenta por club y al crearla quedan vinculados sus equipos.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/login", (LoginCommand command, IAuthRepository auth, CancellationToken ct) => auth.LoginAsync(command, ct))
            .WithName("Login")
            .WithSummary("Inicio de sesión")
            .WithDescription("Devuelve un JWT (caduca según Jwt:ExpirationMinutes) y la cuenta: perfil de jugador con su plantilla, o perfil de técnico con sus plantillas.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/me", (IAuthRepository auth, CancellationToken ct) => auth.GetMyAccountAsync(ct))
            .RequireAuthorization()
            .WithName("GetMyAccount")
            .WithSummary("Cuenta autenticada")
            .WithDescription("Tipo de cuenta y perfil: el jugador con la plantilla a la que está vinculado; el técnico con las plantillas que gestiona y sus nombramientos; el club con sus equipos y nombramientos.");

        return app;
    }
}
