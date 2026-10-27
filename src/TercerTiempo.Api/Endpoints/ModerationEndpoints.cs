using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Moderation;
using TercerTiempo.Repository.Moderation;

namespace TercerTiempo.Api.Endpoints;

internal static class ModerationEndpoints
{
    public static IEndpointRouteBuilder MapModerationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/referees/comments/check", (CommentCheckRequest request, IModerationRepository moderation, CancellationToken ct) =>
                moderation.CheckAsync(request.Text, ct))
            .RequireAuthorization()
            .WithTags("Árbitros")
            .WithName("CheckRefereeComment")
            .WithSummary("Comprobar un comentario antes de puntuar")
            .WithDescription("Dice si el comentario se puede publicar o qué palabras o expresiones de la lista de prohibidas (palabrotas, insultos, vejaciones, amenazas) contiene. El guardado de la nota hace la misma comprobación y responde 400 en el campo comment.");

        var admin = app.MapGroup("/api/admin/moderation").WithTags("Admin").RequireAuthorization(AuthClaims.AdminPolicy);

        admin.MapGet("/referee-comments", (CommentFilter? filter, string? search, IModerationRepository moderation, CancellationToken ct) =>
                moderation.ListCommentsAsync(filter, search, ct))
            .WithName("ListRefereeComments")
            .WithSummary("Comentarios de las notas a los árbitros")
            .WithDescription("Los últimos 500 comentarios, del más reciente al más antiguo, con las palabras de la lista actual que contienen (pueden ser anteriores a ellas) y si están retirados. filter: All, Flagged (con palabras prohibidas), Hidden o Visible; search busca en el comentario, el árbitro, el equipo y quién lo puso. Solo administradores.");

        admin.MapPut("/referee-comments/{ratingId:guid}/hidden", (Guid ratingId, CommentHiddenRequest request, IModerationRepository moderation, CancellationToken ct) =>
                moderation.SetCommentHiddenAsync(ratingId, request.Hidden, ct))
            .WithName("SetRefereeCommentHidden")
            .WithSummary("Retirar o volver a publicar un comentario")
            .WithDescription("Silencia un comentario fuera de lugar: la nota sigue publicada y el texto se conserva, pero en público sale como retirado por la administración. hidden = false lo vuelve a publicar. Solo administradores.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        admin.MapGet("/forbidden-words", (IModerationRepository moderation, CancellationToken ct) => moderation.ListWordsAsync(ct))
            .WithName("ListForbiddenWords")
            .WithSummary("Palabras prohibidas")
            .WithDescription("La tabla PalabrasProhibidas: palabras y expresiones que no se pueden publicar en un comentario (Profanity, Insult, Slur, Threat). Solo administradores.");

        admin.MapPost("/forbidden-words", (AddForbiddenWordCommand command, IModerationRepository moderation, CancellationToken ct) =>
                moderation.AddWordAsync(command, ct))
            .WithName("AddForbiddenWord")
            .WithSummary("Añadir una palabra o expresión prohibida")
            .WithDescription("Se guarda en minúsculas y sin tildes (salvo la ñ); una palabra suelta cuenta también en plural. Vale desde el siguiente comentario. 409 si ya está. Solo administradores.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        admin.MapDelete("/forbidden-words/{term}", (string term, IModerationRepository moderation, CancellationToken ct) => moderation.RemoveWordAsync(term, ct))
            .WithName("RemoveForbiddenWord")
            .WithSummary("Quitar una palabra o expresión prohibida")
            .WithDescription("Deja de bloquearse desde el siguiente comentario. Solo administradores.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        admin.MapPost("/check", (CommentCheckRequest request, IModerationRepository moderation, CancellationToken ct) => moderation.CheckAsync(request.Text, ct))
            .WithName("CheckTextAsAdmin")
            .WithSummary("Probar un texto contra la lista")
            .WithDescription("Lo mismo que se comprueba al guardar un comentario, para probar la lista. Solo administradores.");

        return app;
    }
}
