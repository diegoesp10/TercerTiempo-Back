namespace TercerTiempo.Domain.Common;

// La hora de los clubes: Madrid, con su horario de verano. Las fechas guardadas son UTC, pero «hoy» (hasta cuándo vale un
// nombramiento, cuándo empieza una licencia, la edad de un jugador) y las horas que se escriben en los textos son las de
// aquí, también en un contenedor de Docker, que corre en UTC.
public static class SpainTime
{
    public static readonly TimeZoneInfo Zone = Resolve();

    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Zone).DateTime);

    public static DateTimeOffset Local(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, Zone);

    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { "Europe/Madrid", "Romance Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // Se prueba el siguiente nombre.
            }
            catch (InvalidTimeZoneException)
            {
                // Se prueba el siguiente nombre.
            }
        }
        return TimeZoneInfo.Utc;
    }
}
