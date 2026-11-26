using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Teams;

public static class TeamCrests
{
    // Los mapeadores de equipos son estáticos: la política se fija una vez al arrancar (Crests:ClubUploadsOnly).
    public static bool ClubUploadsOnly { get; set; }

    public static bool HasVisibleCrest(this Team team) => team.CrestKey is not null && (team.CrestFromClub || !ClubUploadsOnly);

    public static string Route(Guid teamId, string? crestKey = null) =>
        $"/api/teams/{teamId}/crest{(crestKey is null ? "" : ImageVersions.Query(crestKey))}";

    public static string? CrestLink(this Team team) => team.HasVisibleCrest() ? Route(team.Id, team.CrestKey) : null;

    public static string PhotoRoute(Guid teamId, Guid entryId, string photoKey) =>
        $"/api/teams/{teamId}/roster/{entryId}/photo{ImageVersions.Query(photoKey)}";

    public static string? PhotoLink(this RosterEntry entry) => entry.PhotoKey is null ? null : PhotoRoute(entry.TeamId, entry.Id, entry.PhotoKey);
}
