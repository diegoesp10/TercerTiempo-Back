using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Players;

public sealed record PlayerDto(
    Guid Id,
    string DisplayName,
    Position Position,
    DateOnly? BirthDate,
    bool LinkedToProvider,
    SquadMembershipDto? Membership);

public sealed record SquadMembershipDto(
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
    MembershipStatus Status,
    bool VerifiedInRoster,
    DateTimeOffset JoinedAt);

internal static class PlayerMapper
{
    public static PlayerDto ToDto(Player player, SquadMembership? membership, Team? team, Competition? competition, Season? season)
    {
        SquadMembershipDto? membershipDto = null;

        if (membership is not null && team is not null && competition is not null && season is not null)
        {
            membershipDto = new SquadMembershipDto(
                team.Id, team.Name, team.ShortName, team.CrestUrl,
                competition.Id, competition.Name, competition.Level,
                season.Id, season.Label,
                membership.ShirtNumber, membership.Status, membership.VerifiedInRoster, membership.JoinedAt);
        }

        return new PlayerDto(
            player.Id, player.DisplayName, player.Position, player.BirthDate,
            player.ExternalRef is not null, membershipDto);
    }

    /// <summary>Resuelve la pertenencia vigente del jugador (equipo, competición y temporada) y construye el DTO.</summary>
    public static async Task<PlayerDto> ToDtoAsync(
        Player player, ICompetitionStore competitions, ITeamStore teams, CancellationToken cancellationToken)
    {
        var membership = player.CurrentMembership;
        if (membership is null)
            return ToDto(player, null, null, null, null);

        var team = await teams.GetByIdAsync(membership.TeamId, cancellationToken);
        var competition = await competitions.GetBySeasonAsync(membership.SeasonId, cancellationToken);
        var season = competition?.Seasons.FirstOrDefault(s => s.Id == membership.SeasonId);

        return ToDto(player, membership, team, competition, season);
    }
}
