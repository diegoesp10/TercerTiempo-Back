using TercerTiempo.Application.Auth.Login;
using TercerTiempo.Application.Auth.Register;
using TercerTiempo.Repository.Auth;

namespace TercerTiempo.Api.Endpoints;

internal static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", (RegisterCommand command, IAuthRepository auth, CancellationToken ct) => auth.RegisterAsync(command, ct))
            .WithName("Register")
            .WithSummary("Alta en la plataforma")
            .WithDescription("Crea el usuario y su perfil según accountType y devuelve el JWT ya iniciado. Player: indica su posición. Coach: necesita el accessCode que le ha entregado su club; al crear la cuenta se canjea y entra en el cuerpo técnico de la plantilla del equipo. Club: el nombre del club tal y como figura en GET /api/clubs/catalog; solo puede haber una cuenta por club y al crearla quedan vinculados sus equipos.")
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
