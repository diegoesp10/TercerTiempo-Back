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
            .WithDescription("Crea el usuario y su perfil (jugador o cuerpo técnico según accountType) y devuelve el JWT ya iniciado. Los jugadores indican su posición.")
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
            .WithDescription("Tipo de cuenta y perfil: el jugador con la plantilla a la que está vinculado; el técnico con las plantillas que gestiona.");

        return app;
    }
}
