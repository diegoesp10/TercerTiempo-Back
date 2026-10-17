using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Players;

namespace TercerTiempo.Domain.Performances;

/// <summary>Reglas que deciden si un jugador puede registrar su rendimiento en un partido.</summary>
public static class PerformancePolicy
{
    public static void EnsureCanLog(Player player, Match match, Guid teamId)
    {
        if (!match.Involves(teamId))
            throw new DomainException("Tu equipo no participa en este partido.");

        if (match.Status != MatchStatus.Played)
            throw new DomainException("Solo se puede registrar el rendimiento de partidos ya disputados.");

        if (!player.IsActiveIn(teamId, match.SeasonId))
            throw new DomainException("No perteneces a la plantilla activa de este equipo en la temporada del partido.");
    }
}
