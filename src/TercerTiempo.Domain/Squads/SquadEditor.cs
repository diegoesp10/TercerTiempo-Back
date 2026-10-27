using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Squads;

// Quien escribe en los datos internos de una plantilla: un técnico vigente o el club que gestiona el equipo, que es la
// entidad de la que depende el cuerpo técnico y puede hacer todo lo que hace él.
public sealed class SquadEditor
{
    private readonly IReadOnlySet<Guid> _clubTeams;

    public Guid? CoachId { get; }
    public Guid? ClubId { get; }

    private SquadEditor(Guid? coachId, Guid? clubId, IReadOnlySet<Guid> clubTeams)
    {
        CoachId = coachId;
        ClubId = clubId;
        _clubTeams = clubTeams;
    }

    public static SquadEditor Coach(Guid coachId) => new(Guard.NotEmpty(coachId, nameof(coachId)), null, new HashSet<Guid>());

    public static SquadEditor Club(Club club) => new(null, club.Id, club.Teams.Select(t => t.TeamId).ToHashSet());

    public bool CanEdit(Squad squad, DateOnly today) =>
        CoachId is { } coachId ? squad.IsStaff(coachId, today) : _clubTeams.Contains(squad.TeamId);
}
