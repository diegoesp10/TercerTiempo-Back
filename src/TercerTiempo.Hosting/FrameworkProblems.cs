using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using TercerTiempo.Application.Abstractions.Errors;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Hosting;

// Lo que rechaza el propio framework antes de llegar al endpoint (un parámetro que falta o no se puede leer, una ruta o un
// método que no existe, sin sesión, sin permiso) sale como los errores de los handlers: título en el idioma de la
// petición, detalle y código del catálogo.
internal static partial class FrameworkProblems
{
    // El framework explica los parámetros en inglés y con su tipo de .NET («Required parameter "Guid competitionId" was
    // not provided from query string.»): de ahí solo se toma el nombre del parámetro.
    [GeneratedRegex("parameter \"(?:[^\"]* )?(?<name>[^\" ]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterName();

    public static (string Code, string? Parameter) FromBadRequest(int status, string message, Exception? inner)
    {
        if (status == StatusCodes.Status413PayloadTooLarge) return (ErrorCode.RequestTooLarge, null);
        if (status == StatusCodes.Status415UnsupportedMediaType) return (ErrorCode.RequestUnsupportedMediaType, null);
        if (inner is JsonException
            || message.Contains("request body", StringComparison.Ordinal)
            || message.StartsWith("Implicit body", StringComparison.Ordinal)
            || message.EndsWith("from body.", StringComparison.Ordinal))
            return (ErrorCode.RequestInvalidBody, null);

        var match = ParameterName().Match(message);
        if (!match.Success) return (ErrorCode.RequestInvalid, null);
        var parameter = JsonNamingPolicy.CamelCase.ConvertName(match.Groups["name"].Value);
        return message.StartsWith("Required parameter", StringComparison.Ordinal)
            ? (ErrorCode.RequestMissingParameter, parameter)
            : (ErrorCode.RequestInvalidParameter, parameter);
    }

    // Con un token que no vale (caducado, de antes de cambiar la contraseña) el 401 dice que la sesión ha caducado; sin
    // token, que hay que entrar. El 403 del administrador es el de sus escrituras: con su cuenta solo se mira.
    public static (string TitleCode, string Code) ForStatus(int status, bool sentToken, bool adminWrite) => status switch
    {
        StatusCodes.Status401Unauthorized => (ErrorCode.TitleUnauthenticated, sentToken ? ErrorCode.AuthSessionExpired : ErrorCode.AuthUnauthenticated),
        StatusCodes.Status403Forbidden => (ErrorCode.TitleForbidden, adminWrite ? ErrorCode.AuthAdminReadOnly : ErrorCode.AuthForbidden),
        StatusCodes.Status404NotFound => (ErrorCode.TitleNotFound, ErrorCode.RequestNotFound),
        StatusCodes.Status405MethodNotAllowed => (ErrorCode.TitleBadRequest, ErrorCode.RequestMethodNotAllowed),
        StatusCodes.Status413PayloadTooLarge => (ErrorCode.TitleBadRequest, ErrorCode.RequestTooLarge),
        StatusCodes.Status415UnsupportedMediaType => (ErrorCode.TitleBadRequest, ErrorCode.RequestUnsupportedMediaType),
        >= StatusCodes.Status500InternalServerError => (ErrorCode.TitleInternal, ErrorCode.CommonUnexpected),
        _ => (ErrorCode.TitleBadRequest, ErrorCode.RequestInvalid),
    };

    // Las respuestas de error que el framework deja sin cuerpo (UseStatusCodePages).
    public static async Task WriteStatusCodeAsync(StatusCodeContext context)
    {
        var http = context.HttpContext;
        var status = http.Response.StatusCode;
        var adminWrite = http.User.HasClaim(AuthClaims.Admin, "true") && !HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method);
        var (titleCode, code) = ForStatus(status, http.Request.Headers.Authorization.Count > 0, adminWrite);

        var messages = http.RequestServices.GetRequiredService<IErrorMessageResolver>();
        var language = GlobalExceptionHandler.Language(http.Request);
        object[] args = status == StatusCodes.Status405MethodNotAllowed ? [http.Request.Method] : [];
        var problem = new ProblemDetails
        {
            Status = status,
            Title = await messages.ResolveAsync(titleCode, language, [], http.RequestAborted),
            Detail = await messages.ResolveAsync(code, language, args, http.RequestAborted),
        };
        problem.Extensions["code"] = code;

        await http.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = problem,
        });
    }
}
