using TercerTiempo.Api.Infrastructure;
using TercerTiempo.Application.Squads.Suggestions;
using TercerTiempo.Application.Players.JoinSquad;
using TercerTiempo.Repository.Players;

namespace TercerTiempo.Api.Endpoints;

internal static class PlayerEndpoints
{
    public static IEndpointRouteBuilder MapPlayerEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/players/{playerId:guid}/photo", async (Guid playerId, HttpContext context, IPlayersRepository players, CancellationToken ct) =>
            {
                var photo = await players.OpenPhotoAsync(playerId, ct);
                if (photo is null) return Results.NotFound();

                context.Response.Headers.CacheControl = "public, max-age=86400";
                return Results.Stream(photo.Content, photo.ContentType);
            })
            .WithTags("Jugadores")
            .WithName("GetPlayerPhoto")
            .WithSummary("Foto de un jugador")
            .WithDescription("Foto que ha subido el propio jugador (photoUrl apunta aquí, con la versión en la URL). 404 si no tiene.")
            .Produces(StatusCodes.Status200OK, contentType: "image/png")
            .ProducesProblem(StatusCodes.Status404NotFound);

        var group = app.MapGroup("/api/players/me").WithTags("Jugadores").RequireAuthorization();

        group.MapPut("/photo", async (IFormFile file, IPlayersRepository players, CancellationToken ct) =>
            {
                await using var content = file.OpenReadStream();
                return await players.UploadPhotoAsync(new TercerTiempo.Application.Clubs.ManageTeams.CrestUpload(file.FileName, file.ContentType, file.Length, content), ct);
            })
            .DisableAntiforgery()
            .WithName("UploadMyPhoto")
            .WithSummary("Subir mi foto")
            .WithDescription("Solo jugadores. multipart/form-data con el campo file (PNG con fondo transparente recomendado; también JPG o WebP), hasta 3 MB. Sustituye la anterior y manda sobre la foto que el club ponga en la ficha.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/public-profile", (PublicProfileRequest request, IPlayersRepository players, CancellationToken ct) => players.SetPublicProfileAsync(request.Public, ct))
            .WithName("SetMyPublicProfile")
            .WithSummary("Hacer público mi perfil completo")
            .WithDescription("Solo jugadores. public=true: cualquier cuenta con sesión ve tu perfil completo (actas de Tercer Tiempo, asistencias de gol, nota; sin las observaciones del técnico); public=false: solo tu club y tu plantilla. La ficha básica con las actas oficiales se ve siempre.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapDelete("/photo", (IPlayersRepository players, CancellationToken ct) => players.DeletePhotoAsync(ct))
            .WithName("DeleteMyPhoto")
            .WithSummary("Quitar mi foto");

        group.MapGet("/invite/{inviteCode}", (string inviteCode, IPlayersRepository players, CancellationToken ct) => players.GetInviteAsync(inviteCode, ct))
            .WithName("GetSquadInvite")
            .WithSummary("Consultar un código de invitación")
            .WithDescription("Cuentas de jugador sin plantilla (por ejemplo tras desvincularse). Devuelve únicamente la ficha asociada al código personal; nunca la lista de fichas libres. Antes del alta se usa GET /api/auth/invite/{code}.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/squad", (JoinSquadAsPlayerCommand command, IPlayersRepository players, CancellationToken ct) => players.JoinSquadAsync(command, ct))
            .WithName("JoinSquadAsPlayer")
            .WithSummary("Vincularse a la ficha de una plantilla")
            .WithDescription("Cuentas de jugador ya existentes: con el código personal de un solo uso generado por su club o entrenador. La API determina la ficha a partir del código (no admite elegir jugador) y la cuenta pasa a llamarse como la ficha. Un jugador solo puede estar vinculado a una plantilla. Las cuentas nuevas se vinculan ya en POST /api/auth/register.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapDelete("/squad", (IPlayersRepository players, CancellationToken ct) => players.LeaveSquadAsync(ct))
            .WithName("LeaveSquadAsPlayer")
            .WithSummary("Desvincularse de la plantilla")
            .WithDescription("La ficha y sus actas siguen en la plantilla; solo se rompe el vínculo con la cuenta.")
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/dashboard", (IPlayersRepository players, CancellationToken ct) => players.GetDashboardAsync(ct))
            .WithName("GetPlayerDashboard")
            .RequireLicense(LicenseScope.Player)
            .WithSummary("Panel del jugador")
            .WithDescription("Estadísticas calculadas a partir de las actas que registra su cuerpo técnico: individuales (totales y por 90'), impacto en el equipo (minutos, cuota de goles, resultados con y sin él) y comparativa con la liga. Los percentiles solo comparan con actas visibles (plantillas públicas y la suya) y aparecen con al menos dos jugadores con minutos.");

        group.MapGet("/performances", (IPlayersRepository players, CancellationToken ct) => players.GetMyPerformancesAsync(ct))
            .WithName("ListMyPerformances")
            .RequireLicense(LicenseScope.Player)
            .WithSummary("Mis actas")
            .WithDescription("Partidos en los que el cuerpo técnico ha registrado la línea del jugador, del más reciente al más antiguo.");

        group.MapPost("/suggestions", (SuggestPerformanceCommand command, IPlayersRepository players, CancellationToken ct) => players.SuggestAsync(command, ct))
            .WithName("SuggestPerformance")
            .RequireLicense(LicenseScope.Player)
            .WithSummary("Sugerir mi línea del acta de un partido")
            .WithDescription("El jugador propone corregir su línea (o crearla si el cuerpo técnico no la registró) con un mensaje. La decide su club o su cuerpo técnico; solo puede haber una pendiente por partido.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/suggestions", (IPlayersRepository players, CancellationToken ct) => players.ListMySuggestionsAsync(ct))
            .WithName("ListMySuggestions")
            .RequireLicense(LicenseScope.Player)
            .WithSummary("Mis sugerencias")
            .WithDescription("Sugerencias del jugador en su plantilla actual, pendientes primero, con la decisión y la nota del club o el técnico.");

        return app;
    }
}

internal sealed record PublicProfileRequest(bool Public);
