using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common;

namespace TercerTiempo.Api.Infrastructure;

/// <summary>Traduce las excepciones de aplicación/dominio a respuestas ProblemDetails con el código HTTP adecuado.</summary>
internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = Map(exception);
        var status = problem.Status ?? StatusCodes.Status500InternalServerError;

        if (status >= 500)
            logger.LogError(exception, "Error no controlado en {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static ProblemDetails Map(Exception exception) => exception switch
    {
        ValidationException ex => new HttpValidationProblemDetails(
            ex.Errors
                .GroupBy(e => JsonNamingPolicy.CamelCase.ConvertName(e.PropertyName ?? string.Empty))
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray()))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Datos no válidos",
        },

        DomainException ex => Problem(StatusCodes.Status422UnprocessableEntity, "Regla de negocio incumplida", ex.Message),
        NotFoundException ex => Problem(StatusCodes.Status404NotFound, "No encontrado", ex.Message),
        ConflictException ex => Problem(StatusCodes.Status409Conflict, "Conflicto", ex.Message),
        ForbiddenException ex => Problem(StatusCodes.Status403Forbidden, "Acceso denegado", ex.Message),
        UnauthenticatedException ex => Problem(StatusCodes.Status401Unauthorized, "No autenticado", ex.Message),
        InvalidCredentialsException ex => Problem(StatusCodes.Status401Unauthorized, "Credenciales incorrectas", ex.Message),

        _ => Problem(StatusCodes.Status500InternalServerError, "Error interno", "Se ha producido un error inesperado."),
    };

    private static ProblemDetails Problem(int status, string title, string detail) =>
        new() { Status = status, Title = title, Detail = detail };
}
