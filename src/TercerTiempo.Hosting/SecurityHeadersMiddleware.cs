using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace TercerTiempo.Hosting;

internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private static readonly PathString[] ProtectedPrefixes = ["/api", "/media"];

    // Las respuestas de la API y del servicio de vídeo son datos (JSON, imágenes, vídeo), nunca páginas: aunque se abran
    // directamente en el navegador, un escudo SVG con <script> no ejecuta nada (sandbox, sin orígenes) ni se puede
    // incrustar en otra web. La documentación (/docs) queda fuera porque Scalar sí es una página.
    private const string ContentSecurityPolicy =
        "default-src 'none'; img-src 'self' data:; style-src 'unsafe-inline'; frame-ancestors 'none'; sandbox";

    public Task InvokeAsync(HttpContext context)
    {
        // Al empezar la respuesta y no antes: el manejador de errores borra las cabeceras al escribir el ProblemDetails.
        if (ProtectedPrefixes.Any(prefix => context.Request.Path.StartsWithSegments(prefix)))
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers.ContentSecurityPolicy = ContentSecurityPolicy;
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                return Task.CompletedTask;
            });

        return next(context);
    }
}

public static class SecurityHeadersExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.UseMiddleware<SecurityHeadersMiddleware>();
}
