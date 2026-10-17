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
            .WithSummary("Alta de un jugador en la plataforma")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/login", (LoginCommand command, IAuthRepository auth, CancellationToken ct) => auth.LoginAsync(command, ct))
            .WithSummary("Inicio de sesión: devuelve un JWT y el perfil del jugador")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }
}
