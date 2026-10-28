namespace TercerTiempo.Infrastructure.Jobs;

// Los temporizadores de .NET solo cuentan el tiempo con el equipo encendido: si se suspende (Docker en un PC que se
// duerme), una espera de 30 minutos termina horas después de la hora prevista y mientras tanto el trabajo no atiende a
// nada. Las esperas largas se hacen a trozos de como mucho un minuto mirando el reloj real, así que al reanudar se
// cumplen enseguida.
internal static class WallClockDelay
{
    public static readonly TimeSpan MaxStep = TimeSpan.FromMinutes(1);

    public static TimeSpan Step(TimeSpan wait) => wait < MaxStep ? wait : MaxStep;

    public static async Task DelayUntilAsync(this TimeProvider clock, DateTimeOffset until, CancellationToken cancellationToken)
    {
        for (var left = until - clock.GetUtcNow(); left > TimeSpan.Zero; left = until - clock.GetUtcNow())
            await Task.Delay(Step(left), clock, cancellationToken);
    }
}
