using Scalar.AspNetCore;
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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.Title = "Tercer Tiempo API");
}
else
{
    app.UseHttpsRedirection();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapTercerTiempoEndpoints();

app.Run();
