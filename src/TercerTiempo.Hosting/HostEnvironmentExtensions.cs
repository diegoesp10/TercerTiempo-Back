using Microsoft.Extensions.Hosting;

namespace TercerTiempo.Hosting;

// Entornos de los procesos web (ASPNETCORE_ENVIRONMENT), cada uno con su appsettings.{Entorno}.json encima de
// appsettings.json: Development, FakeDatos (desarrollo con la base de datos falsos), Pilot (publicación para los
// clubes del piloto) y Production.
public static class HostEnvironmentExtensions
{
    public const string FakeDatos = "FakeDatos";
    public const string Pilot = "Pilot";

    // FakeDatos es desarrollo con otra base: lo que .NET solo hace en Development (user-secrets, /docs abierto, sin
    // redirección a HTTPS) también le toca.
    public static bool IsLocalDevelopment(this IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment(FakeDatos);
}
