using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Domain.Performances;

/// <summary>Reglas que deciden quién puede registrar el acta de un partido y para qué plantilla.</summary>
public static class PerformancePolicy
{
    public static void EnsureCanRecord(Squad squad, Guid coachId, Match match, DateOnly today)
    {
        if (!squad.IsStaff(coachId, today))
            throw new DomainException("Solo el cuerpo técnico vigente de la plantilla puede registrar el acta.");

        if (match.SeasonId != squad.SeasonId)
            throw new DomainException("El partido no pertenece a la temporada de esta plantilla.");

        if (!match.Involves(squad.TeamId))
            throw new DomainException("El equipo de la plantilla no participa en este partido.");

        if (match.Status != MatchStatus.Played)
            throw new DomainException("Solo se puede registrar el acta de partidos ya disputados.");
    }
}
