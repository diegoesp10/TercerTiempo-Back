using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using TercerTiempo.Api.Infrastructure;

namespace TercerTiempo.Api.Extensions;

/// <summary>Sección "OpenApi" de la configuración.</summary>
public sealed class OpenApiDocsOptions
{
    public const string SectionName = "OpenApi";

    /// <summary>null = solo en Development. true/false fuerza el valor en cualquier entorno.</summary>
    public bool? Enabled { get; set; }

    /// <summary>Ruta de la referencia interactiva de la API, sin barras (por defecto "docs").</summary>
    public string RoutePrefix { get; set; } = "docs";

    /// <summary>Credenciales de Basic Auth. Fuera de Development son obligatorias para publicar la documentación.</summary>
    public BasicAuthOptions BasicAuth { get; set; } = new();

    public sealed class BasicAuthOptions
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        public bool IsConfigured => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
    }
}

/// <summary>
/// Documentación OpenAPI nativa de .NET (Microsoft.AspNetCore.OpenApi): el documento se genera a partir de los
/// endpoints y sus metadatos y se publica en /openapi/v1.json; Scalar lo renderiza como referencia interactiva.
/// </summary>
public static class OpenApiExtensions
{
    public const string DocumentName = "v1";
    public const string DocumentRoute = "/openapi/v1.json";

    public static IServiceCollection AddOpenApiDocs(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OpenApiDocsOptions>(configuration.GetSection(OpenApiDocsOptions.SectionName));

        services.AddOpenApi(DocumentName, options =>
        {
            options.AddDocumentTransformer<ApiInfoDocumentTransformer>();
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
            options.AddOperationTransformer<AuthorizationOperationTransformer>();
        });

        return services;
    }

    /// <summary>
    /// Publica el documento OpenAPI (/openapi/v1.json) y la referencia interactiva (/docs).
    /// En Development se publica siempre (sin autenticación salvo que se configure OpenApi:BasicAuth).
    /// En cualquier otro entorno solo con OpenApi:Enabled=true y credenciales de Basic Auth,
    /// que protegen tanto el documento como la referencia.
    /// </summary>
    public static WebApplication UseOpenApiDocs(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<OpenApiDocsOptions>>().Value;
        var isDevelopment = app.Environment.IsDevelopment();

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
            reference.Title = "Tercer Tiempo API";
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
