using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using TercerTiempo.Application.Abstractions.Errors;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Infrastructure.Security;

namespace TercerTiempo.Api.Infrastructure;

internal sealed class ClientThrottlingMiddleware(RequestDelegate next, ClientThrottle throttle, ILogger<ClientThrottlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var settings = throttle.Settings;
        if (!settings.Enabled || HttpMethods.IsOptions(context.Request.Method) || !context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        var ip = ResolveIp(context, settings.TrustForwardedHeaders);
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

    private static string ResolveIp(HttpContext context, bool trustForwarded)
    {
        if (trustForwarded && context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded))
        {
            var first = forwarded.ToString().Split(',')[0].Trim();
            if (IPAddress.TryParse(first, out var parsed)) return parsed.ToString();
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "desconocida";
    }
}

internal static class ClientThrottlingExtensions
{
    public static IApplicationBuilder UseClientThrottling(this IApplicationBuilder app) => app.UseMiddleware<ClientThrottlingMiddleware>();
}
