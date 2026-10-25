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
            .WithDescription("Para cualquier cuenta: si pertenece a algún equipo con licencia vigente (prueba o pagada) y la licencia de cada club al que pertenece (club: la suya, con todos sus equipos; técnico o jugador: la de los clubes de sus plantillas, con solo sus equipos), con su plan, renovación y técnicos de cada equipo. Incluye el catálogo de planes.");

        group.MapGet("/club", (ILicensingRepository licensing, CancellationToken ct) => licensing.GetClubLicenseAsync(ct))
            .WithName("GetClubLicense")
            .WithSummary("Licencia del club")
            .WithDescription("Solo clubes. Una licencia por club, al precio del plan, que cubre el primer equipo y sus filiales; cada equipo tiene su propio cupo de técnicos según el plan. Empieza con la prueba (con todo Premium) al darse de alta y después se paga como suscripción mensual o anual. Sin licencia vigente, el club, su cuerpo técnico y sus jugadores pierden el acceso a los datos privados de sus equipos (no se borran).")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/club/checkout", (LicensePlanRequest request, ILicensingRepository licensing, CancellationToken ct) => licensing.BeginCheckoutAsync(request, ct))
            .WithName("BeginLicenseCheckout")
            .WithSummary("Contratar un plan")
            .WithDescription("Solo clubes sin suscripción en marcha. Crea la sesión de pago de la suscripción del club (primer equipo y filiales, un solo precio) y devuelve la URL a la que llevar al club (Stripe Checkout, o una página del propio frontal con la pasarela simulada). El plan debe admitir el cuerpo técnico de cada equipo. Si queda prueba o periodo pagado, hoy no se cobra nada: el primer cobro llega al acabarse.")
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
            .WithDescription("Pregunta a la pasarela por la sesión y, si está completada, da de alta la suscripción del club. Con Stripe es la vuelta del club tras pagar (el webhook hace lo mismo desde la cola de eventos, lo que llegue antes); con la pasarela simulada, después de pagar en su página. 409 si aún no está pagada. Repetirlo no cambia nada.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/checkout/{sessionId}/simulate", (string sessionId, SimulatedPaymentRequest request, ILicensingRepository licensing, CancellationToken ct) => licensing.SimulatePaymentAsync(sessionId, request, ct))
            .WithName("SimulateLicensePayment")
            .WithSummary("Pagar en la pasarela simulada")
            .WithDescription("Solo con Licensing:Provider = mock y solo el club del pago. Hace de Stripe Checkout con sus tarjetas de prueba: Succeeded completa la sesión y deja en la cola de eventos los mismos avisos que mandaría Stripe (checkout.session.completed y, si se cobra ya, invoice.paid); el resto la rechaza con el código de Stripe (card_declined, insufficient_funds, expired_card, authentication_failed). No se envían datos de tarjeta.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/club/renewal", (LicensePlanRequest request, ILicensingRepository licensing, CancellationToken ct) => licensing.ChangeRenewalAsync(request, ct))
            .WithName("ChangeLicenseRenewal")
            .WithSummary("Cambiar el plan de la renovación")
            .WithDescription("Solo clubes. Cambia el plan o la facturación a partir de la próxima renovación (durante la prueba, ya). El plan debe admitir el cuerpo técnico que tiene cada equipo del club.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/club/renewal/cancel", (ILicensingRepository licensing, CancellationToken ct) => licensing.CancelRenewalAsync(ct))
            .WithName("CancelLicenseRenewal")
            .WithSummary("Cancelar la renovación")
            .WithDescription("Solo clubes. La suscripción deja de renovarse; el club conserva el acceso hasta el final de lo pagado (o de la prueba).");

        group.MapPost("/club/renewal/resume", (ILicensingRepository licensing, CancellationToken ct) => licensing.ResumeRenewalAsync(ct))
            .WithName("ResumeLicenseRenewal")
            .WithSummary("Reactivar la renovación")
            .WithDescription("Solo clubes, mientras la licencia sigue vigente: vuelve a renovarse con el mismo plan.");

        // Avisos de Stripe: el cuerpo se lee tal cual porque la firma se calcula sobre él. Se guardan en la cola de eventos y
        // se responde enseguida; los aplica el proceso de la cola, con reintentos.
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
