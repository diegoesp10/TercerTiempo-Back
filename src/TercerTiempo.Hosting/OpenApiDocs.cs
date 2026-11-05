using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace TercerTiempo.Hosting;

public sealed class OpenApiDocsOptions
{
    public const string SectionName = "OpenApi";

    public bool? Enabled { get; set; }

    public string RoutePrefix { get; set; } = "docs";

    public BasicAuthOptions BasicAuth { get; set; } = new();

    public sealed class BasicAuthOptions
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        public bool IsConfigured => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
    }
}

// Lo que cada proceso cuenta de sí mismo en su documento: título, descripción con el flujo de uso y el orden de los
// grupos en la referencia.
public sealed record OpenApiDocumentInfo(string Title, string Description, IReadOnlyList<(string Name, string Description)> Tags);

public static class OpenApiDocs
{
    public const string DocumentName = "v1";
    public const string DocumentRoute = "/openapi/v1.json";

    public static IServiceCollection AddOpenApiDocs(this IServiceCollection services, IConfiguration configuration, OpenApiDocumentInfo info)
    {
        services.Configure<OpenApiDocsOptions>(configuration.GetSection(OpenApiDocsOptions.SectionName));
        services.AddSingleton(info);

        services.AddOpenApi(DocumentName, options =>
        {
            options.AddDocumentTransformer<InfoDocumentTransformer>();
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
            options.AddOperationTransformer<AuthorizationOperationTransformer>();
        });

        return services;
    }

    // Documento (/openapi/v1.json) y referencia interactiva (/docs): siempre en Development y FakeDatos; en otros
    // entornos solo con OpenApi:Enabled y Basic Auth.
    public static WebApplication UseOpenApiDocs(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<OpenApiDocsOptions>>().Value;
        var info = app.Services.GetRequiredService<OpenApiDocumentInfo>();
        var isDevelopment = app.Environment.IsLocalDevelopment();

        if (!(options.Enabled ?? isDevelopment))
        {
            app.Logger.LogInformation(
                "Documentación OpenAPI desactivada en el entorno {Environment}. Para publicarla configura OpenApi:Enabled=true y OpenApi:BasicAuth.",
                app.Environment.EnvironmentName);
            return app;
        }

        var protect = !isDevelopment || options.BasicAuth.IsConfigured;

        if (protect && !options.BasicAuth.IsConfigured)
        {
            app.Logger.LogWarning(
                "OpenApi está habilitado fuera de Development sin OpenApi:BasicAuth:Username/Password: la documentación NO se publica.");
            return app;
        }

        var prefix = options.RoutePrefix.Trim('/');
        string[] protectedPaths = ["/openapi", $"/{prefix}"];

        if (protect)
        {
            app.UseWhen(
                context => protectedPaths.Any(path => context.Request.Path.StartsWithSegments(path, StringComparison.OrdinalIgnoreCase)),
                branch => branch.UseMiddleware<OpenApiBasicAuthMiddleware>());
        }

        app.MapOpenApi();

        app.MapScalarApiReference($"/{prefix}", reference =>
        {
            reference.Title = info.Title;
            reference.DefaultHttpClient = new(ScalarTarget.CSharp, ScalarClient.HttpClient);
            reference.AddPreferredSecuritySchemes(JwtBearerDefaults.AuthenticationScheme);
        });

        app.MapGet("/", () => Results.Redirect($"/{prefix}")).ExcludeFromDescription();

        app.Logger.LogInformation(
            "Documentación OpenAPI publicada: referencia en /{Prefix} y documento en {Document}{Protection}",
            prefix, DocumentRoute, protect ? " — protegida con Basic Auth" : "");

        return app;
    }
}

internal sealed class InfoDocumentTransformer(OpenApiDocumentInfo info) : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info ??= new OpenApiInfo();
        document.Info.Title = info.Title;
        document.Info.Version = "v1";
        document.Info.Description = info.Description;

        var existing = (document.Tags ?? new HashSet<OpenApiTag>()).ToDictionary(t => t.Name ?? string.Empty);
        var ordered = new HashSet<OpenApiTag>();

        foreach (var (name, description) in info.Tags)
        {
            if (existing.Remove(name, out var tag))
                tag.Description = description;
            else
                tag = new OpenApiTag { Name = name, Description = description };

            ordered.Add(tag);
        }

        foreach (var leftover in existing.Values)
            ordered.Add(leftover);

        document.Tags = ordered;

        return Task.CompletedTask;
    }
}

internal sealed class BearerSecuritySchemeTransformer(IAuthenticationSchemeProvider authenticationSchemeProvider)
    : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var schemes = await authenticationSchemeProvider.GetAllSchemesAsync();
        if (schemes.All(s => s.Name != JwtBearerDefaults.AuthenticationScheme))
            return;

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[JwtBearerDefaults.AuthenticationScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Token devuelto por POST /api/auth/register o POST /api/auth/login. Pega solo el token, sin el prefijo 'Bearer'.",
        };
    }
}

internal sealed class AuthorizationOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;

        var requiresAuthorization = metadata.OfType<IAuthorizeData>().Any()
            && !metadata.OfType<IAllowAnonymous>().Any();

        if (!requiresAuthorization)
            return Task.CompletedTask;

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(JwtBearerDefaults.AuthenticationScheme, context.Document)] = [],
        });

        operation.Responses ??= new OpenApiResponses();
        operation.Responses.TryAdd("401", new OpenApiResponse { Description = "No autenticado: falta el token Bearer o ha caducado." });

        return Task.CompletedTask;
    }
}

internal sealed class OpenApiBasicAuthMiddleware(RequestDelegate next, IOptions<OpenApiDocsOptions> options)
{
    private const string Realm = "Tercer Tiempo docs";

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
