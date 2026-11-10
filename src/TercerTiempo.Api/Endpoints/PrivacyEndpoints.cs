using TercerTiempo.Application.Privacy;
using TercerTiempo.Repository.Admin;
using TercerTiempo.Repository.Privacy;

namespace TercerTiempo.Api.Endpoints;

internal static class PrivacyEndpoints
{
    public static IEndpointRouteBuilder MapPrivacyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Privacidad");

        group.MapGet("/cookies", (IPrivacyRepository privacy, CancellationToken ct) => privacy.GetCookiePolicyAsync(ct))
            .WithName("GetCookiePolicy")
            .WithSummary("Política de cookies")
            .WithDescription("Versión vigente y todo lo que la web guarda en el navegador por categorías: Necessary y Preferences (exentas, siempre activas) y Analytics (solo con consentimiento). Público.");

        group.MapPost("/cookies/consent", (RecordConsentCommand command, IPrivacyRepository privacy, CancellationToken ct) => privacy.RecordConsentAsync(command, ct))
            .WithName("RecordCookieConsent")
            .WithSummary("Guardar la elección de cookies")
            .WithDescription("Sin sesión o con ella (se apunta la cuenta). visitorId es el UUID que el navegador guarda en tt.consent; action AcceptAll, RejectAll o Custom (con analytics). Cada elección se guarda con la versión vigente: si la política cambia, hay que volver a elegir. Público (con el límite de llamadas por IP de toda la API).")
            .ProducesValidationProblem();

        group.MapPost("/analytics/events", (TrackEventsCommand command, IPrivacyRepository privacy, CancellationToken ct) => privacy.TrackAsync(command, ct))
            .WithName("TrackAnalyticsEvents")
            .WithSummary("Analítica propia: páginas vistas, búsquedas y clics")
            .WithDescription("Hasta 50 eventos por envío. Solo se guardan si ese visitante aceptó la analítica con la versión vigente de la política (si no, stored = 0). Quién actúa (anónimo, jugador, técnico, club) sale de la sesión; el club de la página vista, del equipo o la plantilla. Público (con el límite de llamadas por IP de toda la API).")
            .ProducesValidationProblem();

        var admin = app.MapGroup("/api/admin").WithTags("Admin").RequireAuthorization(Application.Abstractions.Identity.AuthClaims.AdminPolicy);

        admin.MapGet("/analytics", (DateOnly? from, DateOnly? to, Guid? clubId, ClubScope? scope, IAdminRepository repo, CancellationToken ct) =>
                repo.GetAnalyticsAsync(new GetAnalyticsReportQuery(from, to, clubId, scope ?? ClubScope.Subject), ct))
            .WithName("GetAnalyticsReport")
            .WithSummary("Uso de la web")
            .WithDescription("De from a to (30 días por defecto): páginas vistas, visitantes, visitas, búsquedas y clics; por día; páginas, búsquedas, clics y equipos/ligas/partidos más vistos; por tipo de usuario; clubes más activos y más visitados; y las elecciones de cookies. Con clubId: scope Actor = lo que hacen sus usuarios, Subject = las visitas a sus páginas. Solo administradores.");

        admin.MapPost("/cookies", (UpsertCookieCommand command, IAdminRepository repo, CancellationToken ct) => repo.UpsertCookieAsync(command with { Id = null }, ct))
            .WithName("CreateCookie")
            .WithSummary("Añadir una cookie al catálogo")
            .WithDescription("Si se añade una de analítica, publica después una versión nueva de la política para volver a pedir el consentimiento. Solo administradores.")
            .ProducesValidationProblem();

        admin.MapPut("/cookies/{id:guid}", (Guid id, UpsertCookieCommand command, IAdminRepository repo, CancellationToken ct) => repo.UpsertCookieAsync(command with { Id = id }, ct))
            .WithName("UpdateCookie")
            .WithSummary("Cambiar una cookie del catálogo")
            .WithDescription("Categoría, almacenamiento, responsable, finalidad, duración y orden (el nombre no cambia). Solo administradores.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        admin.MapDelete("/cookies/{id:guid}", (Guid id, IAdminRepository repo, CancellationToken ct) => repo.DeleteCookieAsync(id, ct))
            .WithName("DeleteCookie")
            .WithSummary("Quitar una cookie del catálogo")
            .WithDescription("Solo administradores.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        admin.MapPost("/cookies/policy", (PublishCookiePolicyCommand command, IAdminRepository repo, CancellationToken ct) => repo.PublishCookiePolicyAsync(command, ct))
            .WithName("PublishCookiePolicy")
            .WithSummary("Publicar una versión nueva de la política de cookies")
            .WithDescription("Los consentimientos de versiones anteriores dejan de valer: el aviso vuelve a salir a todos y la analítica se para hasta que cada uno elija de nuevo. Solo administradores.")
            .ProducesValidationProblem();

        return app;
    }
}
