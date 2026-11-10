using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Repository.Passes;

namespace TercerTiempo.Api.Endpoints;

internal static class PassEndpoints
{
    public static IEndpointRouteBuilder MapPassEndpoints(this IEndpointRouteBuilder app)
    {
        // Pases por partido: lo único que abre lo interno (acta interna y vídeos) a alguien de fuera de la plantilla.
        var squadPasses = app.MapGroup("/api/squads/{squadId:guid}/matches/{matchId:guid}/passes").WithTags("Pases").RequireAuthorization();

        squadPasses.MapGet("/", (Guid squadId, Guid matchId, IPassesRepository passes, CancellationToken ct) => passes.ListMatchPassesAsync(squadId, matchId, ct))
            .WithName("ListMatchPasses")
            .WithSummary("Pases generados para un partido")
            .WithDescription("Solo el club propietario o el cuerpo técnico vigente de la plantilla. Incluye los precios vigentes del partido, si hay acta y vídeo, y cada pase con su código, a quién va, si se canjeó y quién.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        squadPasses.MapPost("/", (Guid squadId, Guid matchId, CreateMatchPassRequest request, IPassesRepository passes, CancellationToken ct) =>
                passes.CreateMatchPassAsync(squadId, matchId, request, ct))
            .WithName("CreateMatchPass")
            .WithSummary("Generar un pase para un partido")
            .WithDescription("Solo el club propietario o el cuerpo técnico vigente, para partidos ya jugados. Devuelve un código de un solo uso (también como enlace {frontal}/pase/{código}) que abre el acta interna, los vídeos o ambos de ese partido a la cuenta que lo canjee; con allowsDownload también puede descargar los vídeos. Sin precio se aplica el del club para ese partido. Caduca si no se canjea en Passes:RedeemDays días.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        squadPasses.MapDelete("/{passId:guid}", (Guid squadId, Guid matchId, Guid passId, IPassesRepository passes, CancellationToken ct) =>
                passes.RevokeMatchPassAsync(squadId, matchId, passId, ct))
            .WithName("RevokeMatchPass")
            .WithSummary("Revocar un pase")
            .WithDescription("Solo el club propietario o el cuerpo técnico vigente. Un pase gratuito se puede revocar aunque ya se haya canjeado (quita el acceso); uno de pago ya canjeado no (409).")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var mine = app.MapGroup("/api/passes").WithTags("Pases").RequireAuthorization();

        mine.MapGet("/mine", (IPassesRepository passes, CancellationToken ct) => passes.ListMineAsync(ct))
            .WithName("ListMyPasses")
            .WithSummary("Mis pases y mis solicitudes")
            .WithDescription("Pases canjeados por la cuenta (con el partido y lo que abren) y sus solicitudes de acceso, con el código listo para canjear de las aprobadas.");

        mine.MapPost("/requests", (PassAccessRequest request, IPassesRepository passes, CancellationToken ct) => passes.CreateRequestAsync(request, ct))
            .WithName("CreatePassRequest")
            .WithSummary("Solicitar acceso a lo interno de un partido")
            .WithDescription("Solo para plantillas públicas de un club, partidos ya jugados y cuentas de fuera de la plantilla. Llega al club como aviso; al aprobarla se genera un pase para quien la pidió, al precio que fije el club.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        mine.MapDelete("/requests/{requestId:guid}", (Guid requestId, IPassesRepository passes, CancellationToken ct) => passes.CancelRequestAsync(requestId, ct))
            .WithName("CancelPassRequest")
            .WithSummary("Retirar una solicitud pendiente")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        mine.MapGet("/{code}", (string code, IPassesRepository passes, CancellationToken ct) => passes.GetByCodeAsync(code, ct))
            .WithName("GetPassByCode")
            .WithSummary("Ver un pase antes de canjearlo")
            .WithDescription("Partido, equipo, club, lo que incluye, precio y estado. No revela quién lo canjeó.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        mine.MapPost("/{code}/redeem", (string code, IPassesRepository passes, CancellationToken ct) => passes.RedeemAsync(code, ct))
            .WithName("RedeemPass")
            .WithSummary("Canjear un pase")
            .WithDescription("Lo deja ligado a esta cuenta para siempre (un solo canje). Si tiene precio lo cobra con la pasarela configurada (de momento simulada) y registra el reparto: Passes:ClubSharePercent para el club y el resto para la plataforma. Los de la propia plantilla no lo necesitan (409).")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var club = app.MapGroup("/api/clubs/me/passes").WithTags("Pases").RequireAuthorization();

        club.MapGet("/requests", (IPassesRepository passes, CancellationToken ct) => passes.ListClubRequestsAsync(ct))
            .WithName("ListClubPassRequests")
            .WithSummary("Solicitudes de acceso recibidas")
            .WithDescription("Solo cuentas de club. Primero las pendientes; después las 50 últimas decididas.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        club.MapPost("/requests/{requestId:guid}/approve", (Guid requestId, ApprovePassRequest request, IPassesRepository passes, CancellationToken ct) =>
                passes.ApproveRequestAsync(requestId, request, ct))
            .WithName("ApprovePassRequest")
            .WithSummary("Aprobar una solicitud y generar su pase")
            .WithDescription("Genera un pase de un solo uso que solo puede canjear quien lo pidió. Sin precio se aplica el del club para ese partido.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        club.MapPost("/requests/{requestId:guid}/reject", (Guid requestId, IPassesRepository passes, CancellationToken ct) => passes.RejectRequestAsync(requestId, ct))
            .WithName("RejectPassRequest")
            .WithSummary("Rechazar una solicitud")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        club.MapGet("/prices", (IPassesRepository passes, CancellationToken ct) => passes.GetClubPricesAsync(ct))
            .WithName("GetClubPassPrices")
            .WithSummary("Precios de los pases del club")
            .WithDescription("Precio por defecto del acta y del vídeo, el reparto (Passes:ClubSharePercent) y cada partido jugado de sus equipos con su precio propio si lo tiene.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        club.MapPut("/prices", (ClubPassPricesRequest request, IPassesRepository passes, CancellationToken ct) => passes.SetClubPricesAsync(request, ct))
            .WithName("SetClubPassPrices")
            .WithSummary("Cambiar los precios por defecto")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        club.MapPut("/prices/squads/{squadId:guid}/matches/{matchId:guid}", (Guid squadId, Guid matchId, MatchPassPriceRequest request, IPassesRepository passes, CancellationToken ct) =>
                passes.SetMatchPriceAsync(squadId, matchId, request, ct))
            .WithName("SetMatchPassPrice")
            .WithSummary("Precio propio de un partido")
            .WithDescription("null en un precio vuelve al del club. Solo partidos jugados de los equipos del club.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        club.MapGet("/earnings", (IPassesRepository passes, CancellationToken ct) => passes.GetClubEarningsAsync(ct))
            .WithName("GetClubPassEarnings")
            .WithSummary("Ventas de pases del club")
            .WithDescription("Totales (bruto, parte del club y parte de la plataforma) y las ventas recientes.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapGet("/api/admin/pass-earnings", (IPassesRepository passes, CancellationToken ct) => passes.GetPlatformEarningsAsync(ct))
            .WithTags("Admin")
            .RequireAuthorization(AuthClaims.AdminPolicy)
            .WithName("GetPlatformPassEarnings")
            .WithSummary("Ventas de pases de toda la plataforma")
            .WithDescription("Solo administrador. Totales y desglose por club de lo vendido, lo que corresponde a cada club y lo de la plataforma.");

        return app;
    }
}
