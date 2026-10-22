using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TercerTiempo.Api.Extensions;

namespace TercerTiempo.Api.Infrastructure;

internal sealed class OpenApiBasicAuthMiddleware(RequestDelegate next, IOptions<OpenApiDocsOptions> options)
{
    private const string Realm = "Tercer Tiempo API docs";

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsAuthorized(context.Request.Headers.Authorization, options.Value.BasicAuth))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = $"Basic realm=\"{Realm}\", charset=\"UTF-8\"";
        await context.Response.WriteAsync("Documentación protegida: introduce las credenciales configuradas en OpenApi:BasicAuth.");
    }

    private static bool IsAuthorized(string? header, OpenApiDocsOptions.BasicAuthOptions expected)
    {
        if (!expected.IsConfigured || string.IsNullOrEmpty(header))
            return false;

        if (!AuthenticationHeaderValue.TryParse(header, out var value)
            || !string.Equals(value.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(value.Parameter))
            return false;

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value.Parameter));
        }
        catch (FormatException)
        {
            return false;
        }

        var separator = decoded.IndexOf(':');
        if (separator < 0)
            return false;

        return FixedTimeEquals(decoded[..separator], expected.Username)
            && FixedTimeEquals(decoded[(separator + 1)..], expected.Password);
    }

    private static bool FixedTimeEquals(string actual, string expected) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected));
}
