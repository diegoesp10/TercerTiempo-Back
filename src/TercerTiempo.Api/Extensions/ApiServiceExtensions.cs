using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using TercerTiempo.Api.Infrastructure;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Infrastructure.Identity;

namespace TercerTiempo.Api.Extensions;

public static class ApiServiceExtensions
{
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.Replace(ServiceDescriptor.Scoped<TercerTiempo.Application.Seasons.ISeasonRequest, SeasonRequest>());

        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        AddJwtAuthentication(services, configuration);
        AddCors(services, configuration);

        services.AddOpenApiDocs(configuration);

        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

        // Detrás de un proxy (Nginx del frontal en Docker): la IP y el esquema del visitante llegan en X-Forwarded-*.
        // El proxy está en la red interna de Docker, así que no se limita a una lista de direcciones conocidas.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        return services;
    }

    private static void AddJwtAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        var keyBytes = Encoding.UTF8.GetBytes(string.IsNullOrEmpty(jwt.Key) ? new string('x', 32) : jwt.Key);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Mantiene los nombres de claim del token (sub, email, player_id) sin mapearlos a ClaimTypes.*
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                };
                options.Events = new JwtBearerEvents
                {
                    // Un token de antes del último cambio de contraseña (otra huella de sesión) ya no vale.
                    OnTokenValidated = async context =>
                    {
                        var sessions = context.HttpContext.RequestServices.GetRequiredService<SessionStamps>();
                        if (!await sessions.IsCurrentAsync(context.Principal, context.HttpContext.RequestAborted))
                            context.Fail("La sesión ya no es válida.");
                    },
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthClaims.AdminPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(AuthClaims.Admin, "true"));

            // El administrador entra en las rutas de las cuentas solo para leer (visor de mantenimiento): con su token
            // cualquier escritura de un club, un técnico o un jugador da 403, además de que no tiene perfil propio.
            options.DefaultPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireAssertion(context => !context.User.HasClaim(AuthClaims.Admin, "true")
                    || context.Resource is HttpContext http && (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method)))
                .Build();
        });
    }

    private static void AddCors(IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() is { Length: > 0 } configured
            ? configured
            : ["http://localhost:5173"];

        services.AddCors(options => options.AddDefaultPolicy(policy =>
            policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));
    }
}
