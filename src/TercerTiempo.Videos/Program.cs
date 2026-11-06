using TercerTiempo.Application;
using TercerTiempo.Hosting;
using TercerTiempo.Infrastructure;
using TercerTiempo.Repository;
using TercerTiempo.Videos.Endpoints;
using TercerTiempo.Videos.Infrastructure;
using TercerTiempo.Videos.Workers;

var builder = WebApplication.CreateBuilder(args);

// Los mismos user-secrets que la API (Jwt:Key, si está ahí); .NET solo los lee solo en Development.
if (builder.Environment.IsEnvironment(HostEnvironmentExtensions.FakeDatos))
    builder.Configuration.AddUserSecrets<Program>(optional: true);

// Sin los trabajos en segundo plano de la API (descargas, copias, renovaciones): este proceso solo tiene el compresor
// y el que lanza el analizador de vídeo.
builder.Services
    .AddApplication()
    .AddRepositoryLayer()
    .AddInfrastructureCore(builder.Configuration)
    .AddWebHost(builder.Configuration)
    .AddOpenApiDocs(builder.Configuration, MediaDocs.Info);
builder.Services.AddHostedService<VideoProcessingWorker>();
builder.Services.AddHostedService<VideoAnalysisWorker>();

var app = builder.Build();

// En Docker va detrás del Nginx del frontal, que termina el TLS y reenvía /media (ReverseProxy:Enabled).
var behindProxy = app.Configuration.GetValue<bool>("ReverseProxy:Enabled");
if (behindProxy)
    app.UseForwardedHeaders();

// Las migraciones las aplica la API al arrancar: este proceso usa la base tal como esté.

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture("es-ES")
    .AddSupportedCultures("es-ES", "en-US")
    .AddSupportedUICultures("es-ES", "en-US"));

app.UseOpenApiDocs();

if (!app.Environment.IsLocalDevelopment() && !behindProxy)
    app.UseHttpsRedirection();

app.UseCors();
app.UseClientThrottling();

app.UseAuthentication();
app.UseAuthorization();

app.MapMediaEndpoints();
app.MapHealthChecks("/media/health").ExcludeFromDescription();

app.Run();
