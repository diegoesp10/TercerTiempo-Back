using TercerTiempo.Api.Infrastructure;
using TercerTiempo.Repository.Licensing;

namespace TercerTiempo.Api.Endpoints;

internal static class LicensingEndpoints
{
    public static IEndpointRouteBuilder MapLicensingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/licensing").WithTags("Licencias").RequireAuthorization();

        group.MapGet("/status", (ILicensingRepository licensing, CancellationToken ct) => licensing.GetStatusAsync(ct))
            .WithName("GetLicensingStatus")
            .WithSummary("Estado de licencia de la cuenta")
            .WithDescription("Para cualquier cuenta: si pertenece a algún equipo con licencia vigente (prueba o pagada) y el estado de cada uno de sus equipos (club: todos los suyos; técnico: sus plantillas; jugador: la suya). Incluye el precio mensual, los días de prueba y la pasarela en uso («mock» = pago simulado).");

        group.MapGet("/club", (ILicensingRepository licensing, CancellationToken ct) => licensing.ListClubLicensesAsync(ct))
            .WithName("ListClubLicenses")
            .WithSummary("Licencias de los equipos del club")
            .WithDescription("Solo clubes. Una licencia por equipo vinculado: empieza con el periodo de prueba al vincular el equipo y después se paga por meses. Sin licencia vigente, el club, su cuerpo técnico y sus jugadores pierden el acceso a los datos privados del equipo (no se borran).")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/club/{teamId:guid}/checkout", (Guid teamId, LicenseCheckoutRequest request, ILicensingRepository licensing, CancellationToken ct) => licensing.BeginCheckoutAsync(teamId, request, ct))
            .WithName("BeginLicenseCheckout")
            .WithSummary("Iniciar el pago de una licencia")
            .WithDescription("Solo clubes, para un equipo suyo. Crea el intento de pago y devuelve la sesión de la pasarela y la URL a la que llevar al club. Con la pasarela simulada la URL es una página del propio frontal que confirma el pago con un clic, sin cobro real.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/checkout/{sessionId}", (string sessionId, ILicensingRepository licensing, CancellationToken ct) => licensing.GetCheckoutAsync(sessionId, ct))
            .WithName("GetLicenseCheckout")
            .WithSummary("Detalle de un intento de pago")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/checkout/{sessionId}/confirm", (string sessionId, ILicensingRepository licensing, CancellationToken ct) => licensing.ConfirmPaymentAsync(sessionId, ct))
            .WithName("ConfirmLicensePayment")
            .WithSummary("Confirmar un pago")
            .WithDescription("Comprueba el pago con la pasarela y, si está hecho, suma los meses pagados a la licencia del equipo. Con Stripe esto lo hará el webhook; con la pasarela simulada se confirma siempre.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
