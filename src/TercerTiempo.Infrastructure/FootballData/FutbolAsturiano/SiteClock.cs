namespace TercerTiempo.Infrastructure.FootballData.FutbolAsturiano;

/// <summary>
/// Conversión de las fechas locales del sitio a UTC. futbolasturiano.es guarda en <c>date</c> la fecha y hora reales
/// del partido (hora peninsular), mientras que <c>date_gmt</c> se queda a menudo con la fecha provisional con la que se
/// creó el calendario (domingo a las 12:00), así que la referencia buena es <c>date</c> en la zona del sitio.
/// </summary>
internal static class SiteClock
{
    public static TimeZoneInfo Resolve(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException) when (TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out var windowsId))
        {
            return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
        }
    }

    public static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo timeZone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        // La hora que "no existe" al adelantar el reloj en primavera se desplaza a la siguiente válida.
        if (timeZone.IsInvalidTime(unspecified))
            unspecified = unspecified.AddHours(1);

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecified, timeZone), TimeSpan.Zero);
    }
}
