using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Tests;

// Lo que SquadStore.GetAccessAsync saca con EXISTS, calculado con las reglas del dominio sobre la plantilla en memoria
// (SquadAccessSqlTests comprueba contra SQL Server que la consulta da lo mismo).
internal static class SquadAccessStub
{
    public static Task<SquadAccessRow?> For(Squad squad, IEnumerable<Club> clubs, object?[] args)
    {
        if ((Guid)args[0]! != squad.Id) return Task.FromResult<SquadAccessRow?>(null);

        var coachId = (Guid?)args[1];
        var playerId = (Guid?)args[2];
        var clubId = (Guid?)args[3];
        var today = (DateOnly)args[4]!;
        return Task.FromResult<SquadAccessRow?>(new SquadAccessRow(
            squad.Id, squad.TeamId, squad.SeasonId,
            coachId is { } coach && squad.IsStaff(coach, today),
            playerId is { } player && squad.IsMember(player),
            clubId is { } id && clubs.Any(c => c.Id == id && c.Manages(squad.TeamId))));
    }
}
