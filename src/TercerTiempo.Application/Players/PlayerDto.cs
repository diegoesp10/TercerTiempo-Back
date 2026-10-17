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
    Guid SeasonId,
    string SeasonLabel,
    int? ShirtNumber,
    MembershipStatus Status,
    bool VerifiedInRoster,
    DateTimeOffset JoinedAt);

internal static class PlayerMapper
{
    public static PlayerDto ToDto(Player player, SquadMembership? membership, Team? team, Season? season)
    {
        SquadMembershipDto? membershipDto = null;

        if (membership is not null && team is not null && season is not null)
        {
            membershipDto = new SquadMembershipDto(
                team.Id, team.Name, team.ShortName, team.CrestUrl,
                season.Id, season.Label,
                membership.ShirtNumber, membership.Status, membership.VerifiedInRoster, membership.JoinedAt);
        }

        return new PlayerDto(
            player.Id, player.DisplayName, player.Position, player.BirthDate,
            player.ExternalRef is not null, membershipDto);
    }

    /// <summary>Resuelve la pertenencia del jugador en la temporada activa (si la hay) y construye el DTO.</summary>
    public static async Task<PlayerDto> ToDtoAsync(
        Player player, ICompetitionStore competitions, ITeamStore teams, CancellationToken cancellationToken)
    {
        var competition = await competitions.GetCurrentAsync(cancellationToken);
        var season = competition?.CurrentSeason;
        var membership = season is null ? null : player.MembershipFor(season.Id);
        var team = membership is null ? null : await teams.GetByIdAsync(membership.TeamId, cancellationToken);

        return ToDto(player, membership, team, season);
    }
}
