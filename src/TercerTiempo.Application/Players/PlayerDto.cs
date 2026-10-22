using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

using TercerTiempo.Application.Teams;

namespace TercerTiempo.Application.Players;

public sealed record PlayerDto(
    Guid Id,
    string DisplayName,
    Position Position,
    DateOnly? BirthDate,
    PlayerSquadDto? Squad);

public sealed record PlayerSquadDto(
    Guid SquadId,
    Guid SquadPlayerId,
    Guid TeamId,
    string TeamName,
    string TeamShortName,
    string? TeamCrestUrl,
    Guid CompetitionId,
    string CompetitionName,
    CompetitionLevel CompetitionLevel,
    Guid SeasonId,
    string SeasonLabel,
    int? ShirtNumber,
    SquadVisibility Visibility);

internal static class PlayerMapper
{
    public static PlayerDto ToDto(Player player, Squad? squad, Team? team, Competition? competition, Season? season)
    {
        PlayerSquadDto? squadDto = null;

        if (squad is not null && team is not null && competition is not null && season is not null
            && squad.PlayerEntryOf(player.Id) is { } entry)
        {
            squadDto = new PlayerSquadDto(
                squad.Id, entry.Id,
                team.Id, team.Name, team.ShortName, team.CrestLink(),
                competition.Id, competition.Name, competition.Level,
                season.Id, season.Label,
                entry.ShirtNumber, squad.Visibility);
        }

        return new PlayerDto(player.Id, player.DisplayName, player.Position, player.BirthDate, squadDto);
    }

    public static async Task<PlayerDto> ToDtoAsync(
        Player player, ISquadStore squads, ITeamStore teams, ICompetitionStore competitions, CancellationToken cancellationToken)
    {
        var squad = await squads.GetByPlayerAsync(player.Id, cancellationToken);
        if (squad is null)
            return ToDto(player, null, null, null, null);

        var team = await teams.GetByIdAsync(squad.TeamId, cancellationToken);
        var competition = await competitions.GetBySeasonAsync(squad.SeasonId, cancellationToken);
        var season = competition?.Seasons.FirstOrDefault(s => s.Id == squad.SeasonId);

        return ToDto(player, squad, team, competition, season);
    }
}
