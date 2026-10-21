using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using TercerTiempo.Application.Abstractions.Errors;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Api.Infrastructure;

/// <summary>
/// Traduce las excepciones de aplicación/dominio a ProblemDetails. Los mensajes no van en el código: cada excepción
/// lleva un código de error (<see cref="ErrorCode"/>) y sus argumentos, y el texto se resuelve desde la tabla
/// ErrorMessages en el idioma de la cabecera Accept-Language (español si no hay traducción). La respuesta incluye
/// el código en la extensión <c>code</c> para que el cliente pueda tratarlo sin depender del texto.
/// </summary>
internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // El manejador es singleton y el resolutor (DbContext) es por petición: se pide al ámbito de la petición.
        var messages = httpContext.RequestServices.GetRequiredService<IErrorMessageResolver>();
        var language = Language(httpContext.Request);
        var problem = await MapAsync(messages, exception, language, cancellationToken);
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

    private static async Task<ProblemDetails> MapAsync(IErrorMessageResolver messages, Exception exception, string language, CancellationToken cancellationToken)
    {
        switch (exception)
        {
            case ValidationException ex:
                return new HttpValidationProblemDetails(await ErrorsByFieldAsync(messages, ex.Errors, language, cancellationToken))
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = await TitleAsync(messages, ErrorCode.TitleValidation, language, cancellationToken),
                };

            case CodedException ex:
                var (status, titleCode) = ex switch
                {
                    NotFoundException => (StatusCodes.Status404NotFound, ErrorCode.TitleNotFound),
                    ConflictException => (StatusCodes.Status409Conflict, ErrorCode.TitleConflict),
                    ForbiddenException => (StatusCodes.Status403Forbidden, ErrorCode.TitleForbidden),
                    UnauthenticatedException => (StatusCodes.Status401Unauthorized, ErrorCode.TitleUnauthenticated),
                    InvalidCredentialsException => (StatusCodes.Status401Unauthorized, ErrorCode.TitleInvalidCredentials),
                    _ => (StatusCodes.Status422UnprocessableEntity, ErrorCode.TitleDomain),
                };
                var problem = Problem(status, await TitleAsync(messages, titleCode, language, cancellationToken), await messages.ResolveAsync(ex.Code, language, ex.Args, cancellationToken));
                problem.Extensions["code"] = ex.Code;
                return problem;

            // Cuerpo JSON malformado, enum desconocido, parámetro obligatorio ausente... (en Development el framework los lanza).
            case BadHttpRequestException ex:
                return Problem(
                    ex.StatusCode,
                    await TitleAsync(messages, ErrorCode.TitleBadRequest, language, cancellationToken),
                    ex.InnerException is JsonException json ? $"{ex.Message} {json.Message}" : ex.Message);

            default:
                return Problem(
                    StatusCodes.Status500InternalServerError,
                    await TitleAsync(messages, ErrorCode.TitleInternal, language, cancellationToken),
                    await messages.ResolveAsync(ErrorCode.CommonUnexpected, language, [], cancellationToken));
        }
    }

    /// <summary>
    /// Mensajes de validación por campo (camelCase). Si la regla lleva un código del catálogo (WithErrorCode) se
    /// resuelve por idioma, con los argumentos que la regla dejó en CustomState; si no, se deja el texto de FluentValidation.
    /// </summary>
    private static async Task<Dictionary<string, string[]>> ErrorsByFieldAsync(IErrorMessageResolver messages, IEnumerable<ValidationFailure> failures, string language, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, List<string>>();

        foreach (var failure in failures)
        {
            var field = JsonNamingPolicy.CamelCase.ConvertName(failure.PropertyName ?? string.Empty);
            var message = failure.ErrorCode is { } code && ErrorCatalog.Contains(code)
                ? await messages.ResolveAsync(code, language, failure.CustomState as object[] ?? [], cancellationToken)
                : failure.ErrorMessage;

            if (!result.TryGetValue(field, out var list))
                result[field] = list = [];
            if (!list.Contains(message))
                list.Add(message);
        }

        return result.ToDictionary(e => e.Key, e => e.Value.ToArray());
    }

    private static Task<string> TitleAsync(IErrorMessageResolver messages, string code, string language, CancellationToken cancellationToken) =>
        messages.ResolveAsync(code, language, [], cancellationToken);

    /// <summary>Idioma principal de Accept-Language ("es-ES,es;q=0.9" → "es"); español si no viene.</summary>
    internal static string Language(HttpRequest request)
    {
        var header = request.Headers[HeaderNames.AcceptLanguage].ToString();
        if (string.IsNullOrWhiteSpace(header)) return ErrorMessage.DefaultLanguage;

        var first = header.Split(',')[0].Split(';')[0].Trim();
        var primary = first.Split('-')[0].ToLowerInvariant();
        return primary.Length is >= 2 and <= 8 ? primary : ErrorMessage.DefaultLanguage;
    }

    private static ProblemDetails Problem(int status, string title, string detail) =>
        new() { Status = status, Title = title, Detail = detail };
}
