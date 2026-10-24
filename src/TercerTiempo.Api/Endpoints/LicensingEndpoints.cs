using TercerTiempo.Repository.Licensing;

namespace TercerTiempo.Api.Endpoints;

internal static class LicensingEndpoints
{
    public static IEndpointRouteBuilder MapLicensingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/licensing").WithTags("Licencias").RequireAuthorization();

        group.MapGet("/plans", (ILicensingRepository licensing, CancellationToken ct) => licensing.GetCatalogAsync(ct))
            .AllowAnonymous()
            .WithName("GetLicensingPlans")
            .WithSummary("Planes y precios")
            .WithDescription("Público. Los planes de licencia (Básica y Premium) con su precio mensual y anual y los técnicos que admite cada uno, los días de prueba y la pasarela en uso («mock» = pago simulado).");

        group.MapGet("/status", (ILicensingRepository licensing, CancellationToken ct) => licensing.GetStatusAsync(ct))
            .WithName("GetLicensingStatus")
            .WithSummary("Estado de licencia de la cuenta")
            .WithDescription("Para cualquier cuenta: si pertenece a algún equipo con licencia vigente (prueba o pagada) y el estado de cada uno de sus equipos (club: todos los suyos; técnico: sus plantillas; jugador: la suya), con su plan, renovación y técnicos. Incluye el catálogo de planes.");

        group.MapGet("/club", (ILicensingRepository licensing, CancellationToken ct) => licensing.ListClubLicensesAsync(ct))
            .WithName("ListClubLicenses")
            .WithSummary("Licencias de los equipos del club")
            .WithDescription("Solo clubes. Una licencia por equipo vinculado: empieza con la prueba (con todo Premium) al vincular el equipo y después se paga como suscripción mensual o anual. Sin licencia vigente, el club, su cuerpo técnico y sus jugadores pierden el acceso a los datos privados del equipo (no se borran).")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/club/{teamId:guid}/checkout", (Guid teamId, LicensePlanRequest request, ILicensingRepository licensing, CancellationToken ct) => licensing.BeginCheckoutAsync(teamId, request, ct))
            .WithName("BeginLicenseCheckout")
            .WithSummary("Contratar un plan")
            .WithDescription("Solo clubes, para un equipo suyo sin suscripción en marcha. Crea la sesión de pago de la suscripción y devuelve la URL a la que llevar al club (Stripe Checkout, o una página del propio frontal con la pasarela simulada). Si al equipo le queda prueba o periodo pagado, hoy no se cobra nada: el primer cobro llega al acabarse.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/checkout/{sessionId}", (string sessionId, ILicensingRepository licensing, CancellationToken ct) => licensing.GetCheckoutAsync(sessionId, ct))
            .WithName("GetLicenseCheckout")
            .WithSummary("Detalle de un pago")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/checkout/{sessionId}/confirm", (string sessionId, ILicensingRepository licensing, CancellationToken ct) => licensing.ConfirmPaymentAsync(sessionId, ct))
            .WithName("ConfirmLicensePayment")
            .WithSummary("Confirmar un pago")
            .WithDescription("Pregunta a la pasarela por la sesión y, si está completada, da de alta la suscripción del equipo. Con Stripe es la vuelta del club tras pagar (el webhook hace lo mismo, lo que llegue antes); con la pasarela simulada se confirma siempre. Repetirlo no cambia nada.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/club/{teamId:guid}/renewal", (Guid teamId, LicensePlanRequest request, ILicensingRepository licensing, CancellationToken ct) => licensing.ChangeRenewalAsync(teamId, request, ct))
            .WithName("ChangeLicenseRenewal")
            .WithSummary("Cambiar el plan de la renovación")
            .WithDescription("Solo clubes. Cambia el plan o la facturación a partir de la próxima renovación (durante la prueba, ya). El plan debe admitir el cuerpo técnico que tiene el equipo.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/club/{teamId:guid}/renewal/cancel", (Guid teamId, ILicensingRepository licensing, CancellationToken ct) => licensing.CancelRenewalAsync(teamId, ct))
            .WithName("CancelLicenseRenewal")
            .WithSummary("Cancelar la renovación")
            .WithDescription("Solo clubes. La suscripción deja de renovarse; el equipo conserva el acceso hasta el final de lo pagado (o de la prueba).");

        group.MapPost("/club/{teamId:guid}/renewal/resume", (Guid teamId, ILicensingRepository licensing, CancellationToken ct) => licensing.ResumeRenewalAsync(teamId, ct))
            .WithName("ResumeLicenseRenewal")
            .WithSummary("Reactivar la renovación")
            .WithDescription("Solo clubes, mientras la licencia sigue vigente: vuelve a renovarse con el mismo plan.");

        // Avisos de Stripe: el cuerpo se lee tal cual porque la firma se calcula sobre él.
        group.MapPost("/stripe/webhook", async (HttpRequest request, ILicensingRepository licensing, CancellationToken ct) =>
            {
                using var reader = new StreamReader(request.Body);
                var payload = await reader.ReadToEndAsync(ct);
                await licensing.HandleWebhookAsync(payload, request.Headers["Stripe-Signature"].ToString(), ct);
                return Results.Ok();
            })
            .AllowAnonymous()
            .ExcludeFromDescription()
            .WithName("StripeLicenseWebhook");

        return app;
    }
}
