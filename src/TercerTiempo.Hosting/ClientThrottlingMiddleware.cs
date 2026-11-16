using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using TercerTiempo.Application.Abstractions.Errors;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Infrastructure.Security;

namespace TercerTiempo.Hosting;

internal sealed class ClientThrottlingMiddleware(RequestDelegate next, ClientThrottle throttle, ILogger<ClientThrottlingMiddleware> logger)
{
    // Las rutas de la API y las del servicio de vídeo; lo demás (documentación, salud) no cuenta.
    private static readonly PathString[] ThrottledPrefixes = ["/api", "/media"];

    public async Task InvokeAsync(HttpContext context)
    {
        var settings = throttle.Settings;
        if (!settings.Enabled || HttpMethods.IsOptions(context.Request.Method) || !ThrottledPrefixes.Any(prefix => context.Request.Path.StartsWithSegments(prefix)))
        {
            await next(context);
            return;
        }

        // Detrás del proxy, UseForwardedHeaders ya ha puesto aquí la IP del visitante (ReverseProxy:ForwardLimit saltos de
        // confianza): un X-Forwarded-For inventado por el cliente no la cambia.
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "desconocida";
        var pattern = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? context.Request.Path.Value ?? "/";
        var countEndpoint = !settings.ExemptRoutes.Any(route => string.Equals(route, pattern, StringComparison.OrdinalIgnoreCase));
        var decision = throttle.Check(ip, $"{context.Request.Method} {pattern}", countEndpoint);

        if (decision.Allowed)
        {
            await next(context);
            return;
        }

        if (decision.Reason == ThrottleReason.Limited)
            logger.LogWarning("IP {Ip} bloqueada {Minutes} min (reincidencia {Strikes}) por superar {Limit} llamadas en {Window}s en {Endpoint}",
                ip, (int)decision.RetryAfter.TotalMinutes, decision.Strikes, decision.Limit, decision.WindowSeconds, pattern);

        var language = GlobalExceptionHandler.Language(context.Request);
        var messages = context.RequestServices.GetRequiredService<IErrorMessageResolver>();
        var minutes = Math.Max(1, (int)Math.Ceiling(decision.RetryAfter.TotalMinutes));
        var (code, args) = decision.Reason == ThrottleReason.Banned
            ? (ErrorCode.ThrottleBanned, new object[] { minutes })
            : (ErrorCode.ThrottleTooManyRequests, new object[] { decision.Limit, decision.WindowSeconds, minutes });

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers[HeaderNames.RetryAfter] = ((int)Math.Ceiling(decision.RetryAfter.TotalSeconds)).ToString();

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = await messages.ResolveAsync(ErrorCode.TitleTooManyRequests, language, [], context.RequestAborted),
            Detail = await messages.ResolveAsync(code, language, args, context.RequestAborted),
        };
        problem.Extensions["code"] = code;

        await context.RequestServices.GetRequiredService<IProblemDetailsService>()
            .WriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
    }

}

public static class ClientThrottlingExtensions
{
    public static IApplicationBuilder UseClientThrottling(this IApplicationBuilder app) => app.UseMiddleware<ClientThrottlingMiddleware>();
}
