using TercerTiempo.Api.Endpoints;
using TercerTiempo.Api.Extensions;
using TercerTiempo.Application;
using TercerTiempo.Hosting;
using TercerTiempo.Infrastructure;
using TercerTiempo.Infrastructure.Identity;
using TercerTiempo.Repository;

var builder = WebApplication.CreateBuilder(args);

// .NET solo lee los user-secrets en Development; FakeDatos usa los mismos (la contraseña del administrador).
if (builder.Environment.IsEnvironment(HostEnvironmentExtensions.FakeDatos))
    builder.Configuration.AddUserSecrets<Program>(optional: true);

builder.Services
    .AddApplication()
    .AddRepositoryLayer()
    .AddInfrastructure(builder.Configuration)
    .AddApi(builder.Configuration);

var app = builder.Build();

// En Docker la API va detrás del Nginx del frontal, que termina el TLS (ReverseProxy:Enabled).
var behindProxy = app.Configuration.GetValue<bool>("ReverseProxy:Enabled");
if (behindProxy)
    app.UseForwardedHeaders();

// Aplica migraciones pendientes antes de aceptar tráfico (el worker de sincronización arranca después). El servicio
// de vídeo no migra: espera a que la API lo haya hecho.
await app.Services.MigrateDatabaseAsync();

// Rol Admin y usuario administrador de la configuración (Admin:UserName / Admin:Password en user-secrets) para /jobs.
await app.Services.SeedAdminAsync();

// Beta cerrada: solo los escudos subidos por los clubes (Crests:ClubUploadsOnly).
app.Services.ApplyCrestPolicy();

// Las actas antiguas (o sin nota) reciben la nota calculada con sus datos.
await app.Services.RecalculateMissingRatingsAsync();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Idioma de la petición (Accept-Language): los mensajes de error salen de la tabla ErrorMessages en ese idioma
// (español por defecto) y FluentValidation usa la misma cultura para sus mensajes genéricos.
app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture("es-ES")
    .AddSupportedCultures("es-ES", "en-US")
    .AddSupportedUICultures("es-ES", "en-US"));

// Documento OpenAPI (/openapi/v1.json) y referencia interactiva (/docs): siempre en Development y FakeDatos;
// en otros entornos solo con OpenApi:Enabled y Basic Auth.
app.UseOpenApiDocs();

if (!app.Environment.IsLocalDevelopment() && !behindProxy)
    app.UseHttpsRedirection();

app.UseCors();

// Límite de llamadas por IP y por endpoint (429 + Retry-After; bloqueos de 5 min, 30 min, 1 día... por reincidencia).
app.UseClientThrottling();

app.UseAuthentication();
app.UseAuthorization();

app.MapTercerTiempoEndpoints();
app.MapHealthChecks("/api/health").ExcludeFromDescription();

app.Run();
