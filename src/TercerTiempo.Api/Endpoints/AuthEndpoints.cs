using TercerTiempo.Application.Auth.Login;
using TercerTiempo.Application.Auth.RegisterPlayer;
using TercerTiempo.Repository.Auth;

namespace TercerTiempo.Api.Endpoints;

internal static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", (RegisterPlayerCommand command, IAuthRepository auth, CancellationToken ct) => auth.RegisterAsync(command, ct))
            .WithName("RegisterPlayer")
            .WithSummary("Alta de un jugador en la plataforma")
            .WithDescription("Crea el usuario y su perfil de jugador y devuelve el JWT ya iniciado. Usa el nombre con el que apareces en la plantilla de tu equipo para que la verificación sea automática.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/login", (LoginCommand command, IAuthRepository auth, CancellationToken ct) => auth.LoginAsync(command, ct))
            .WithName("Login")
            .WithSummary("Inicio de sesión")
            .WithDescription("Devuelve un JWT (caduca según Jwt:ExpirationMinutes) y el perfil del jugador con su pertenencia en la temporada actual.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }
}
