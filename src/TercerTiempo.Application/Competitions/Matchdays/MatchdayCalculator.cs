using TercerTiempo.Domain.Matches;

namespace TercerTiempo.Application.Competitions.Matchdays;

/// <summary>Cálculos sobre las jornadas de una temporada. Funciones puras: fáciles de testear.</summary>
internal static class MatchdayCalculator
{
    /// <summary>Margen tras la fecha de una jornada durante el que sigue siendo "la jornada en curso" (el lunes cambia).</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromDays(1);

    /// <summary>
    /// Jornada en curso o próxima: la primera cuya fecha de referencia (la mediana de sus partidos, para que un
    /// partido aplazado a otro mes no la arrastre) no quedó atrás hace más de <see cref="Grace"/>. Si ya se jugaron
    /// todas, la última. Null si no hay partidos.
    /// </summary>
    public static int? Current(IEnumerable<Match> matches, DateTimeOffset now)
    {
        var summaries = Summaries(matches);
        if (summaries.Count == 0) return null;

        foreach (var summary in summaries)
        {
            if (summary.ReferenceAt >= now - Grace)
                return summary.Number;
        }

        return summaries[^1].Number;
    }

    /// <summary>Resumen de cada jornada (fechas, partidos y jugados), ordenadas por número.</summary>
    public static IReadOnlyList<MatchdaySummaryDto> Summaries(IEnumerable<Match> matches) =>
        matches
            .GroupBy(m => m.Matchday)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var kickoffs = g.Select(m => m.KickoffAt).Order().ToList();
                return new MatchdaySummaryDto(
                    g.Key,
                    kickoffs[0],
                    kickoffs[^1],
                    kickoffs[kickoffs.Count / 2],
                    g.Count(),
                    g.Count(m => m.Status == MatchStatus.Played));
            })
            .ToList();
}
