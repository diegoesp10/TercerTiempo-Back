using TercerTiempo.Api.Endpoints;
using TercerTiempo.Api.Extensions;
using TercerTiempo.Application;
using TercerTiempo.Infrastructure;
using TercerTiempo.Repository;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddRepositoryLayer()
    .AddInfrastructure(builder.Configuration)
    .AddApi(builder.Configuration);

var app = builder.Build();

// Aplica migraciones pendientes antes de aceptar tráfico (el worker de sincronización arranca después).
await app.Services.MigrateDatabaseAsync();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Documento OpenAPI (/openapi/v1.json) y referencia interactiva (/docs): siempre en Development;
// en otros entornos solo con OpenApi:Enabled y Basic Auth.
app.UseOpenApiDocs();

if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapTercerTiempoEndpoints();

app.Run();
