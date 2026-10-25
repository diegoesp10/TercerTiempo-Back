using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Clubs.Roster;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Players.Photos;
using TercerTiempo.Application.Teams;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Players.Card;

public sealed record PlayerCardTeamDto(Guid Id, string Name, string ShortName, string? CrestUrl, string? CompetitionName, string? SeasonLabel);

public sealed record PlayerCardDto(
    [property: Description("Datos básicos de la ficha y totales de la temporada según las actas oficiales.")]
    RosterEntryDto Entry,
    PlayerCardTeamDto Team,
    [property: Description("Partidos en los que aparece en el acta oficial (públicos), de la jornada más reciente a la primera.")]
    IReadOnlyList<RosterOfficialAppearanceDto> Official,
    [property: Description("true si la ficha está vinculada a la cuenta del jugador.")]
    bool HasAccount,
    [property: Description("true si el jugador ha hecho público su perfil completo.")]
    bool ProfilePublic,
    [property: Description("true si quien mira ve el perfil completo: es de su club o su plantilla, o el jugador lo ha hecho público.")]
    bool Extended,
    [property: Description("Actas de Tercer Tiempo del cuerpo técnico; vacío si no se ve el perfil completo.")]
    IReadOnlyList<RosterInternalAppearanceDto> Internal,
    RosterInternalStatsDto? InternalStats,
    [property: Description("Perfil interno en la plantilla (gráficos, comparador) para quien es de su club o su plantilla.")]
    string? InternalProfileLink,
    [property: Description("true si quien mira es el propio jugador (puede cambiar si su perfil es público).")]
    bool IsMe);

public sealed record GetPlayerCardQuery(Guid TeamId, Guid EntryId) : IQuery<PlayerCardDto>;

// Ficha de cualquier jugador para cualquier cuenta con sesión: lo básico y las actas oficiales siempre; el perfil
// completo (actas de Tercer Tiempo) solo para su club y su plantilla o si el propio jugador lo ha hecho público.
internal sealed class GetPlayerCardHandler(
    ICurrentUser currentUser,
    RosterStores roster,
    ISquadStore squads,
    IPerformanceStore performances,
    IPlayerStore players,
    ICompetitionStore competitions,
    IClubStore clubs,
    TimeProvider clock) : IQueryHandler<GetPlayerCardQuery, PlayerCardDto>
{
    public async Task<PlayerCardDto> Handle(GetPlayerCardQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated) throw new UnauthenticatedException();

        var entry = await roster.Teams.GetRosterEntryAsync(query.EntryId, cancellationToken);
        if (entry is null || entry.TeamId != query.TeamId || entry.Hidden)
            throw new NotFoundException(ErrorCode.RosterEntryNotFound, query.EntryId);
        var team = await roster.Teams.GetByIdAsync(entry.TeamId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.TeamNotFound, entry.TeamId);
        var competition = await competitions.GetBySeasonAsync(entry.SeasonId, cancellationToken);
        var season = competition?.Seasons.FirstOrDefault(s => s.Id == entry.SeasonId);

        var details = await RosterEntryDetails.BuildAsync(entry.TeamId, entry.SeasonId, entry, roster, squads, performances, cancellationToken);
        var account = details.Player?.PlayerId is { } playerId ? await players.GetByIdAsync(playerId, cancellationToken) : null;
        var insider = details.Squad is not null && await SquadAccess.IsInsiderAsync(details.Squad, currentUser, clock, clubs, cancellationToken);
        var isMe = account is not null && currentUser.PlayerId == account.Id;
        var extended = insider || account?.PublicProfile == true;

        // La foto de la cuenta del jugador manda sobre la que el club pone en la ficha.
        var basic = account?.PhotoLink() is { } photo ? details.Entry with { PhotoUrl = photo } : details.Entry;

        return new PlayerCardDto(
            basic,
            new PlayerCardTeamDto(team.Id, team.Name, team.ShortName, team.CrestLink(), competition?.Name, season?.Label),
            details.Official,
            account is not null,
            account?.PublicProfile == true,
            extended,
            extended ? Visible(details.Internal, insider) : [],
            extended ? RosterEntryDetails.InternalStats(details.Internal) : null,
            insider && details.Squad is not null && details.Player is not null ? $"/plantillas/{details.Squad.Id}/jugadores/{details.Player.Id}" : null,
            isMe);
    }

    // Las observaciones del técnico son del cuerpo técnico: no salen en el perfil público.
    private static IReadOnlyList<RosterInternalAppearanceDto> Visible(IReadOnlyList<RosterInternalAppearanceDto> rows, bool insider) =>
        insider ? rows : rows.Select(r => r with { Performance = r.Performance with { Notes = null } }).ToList();
}

public sealed record SetMyPublicProfileCommand(
    [property: Description("true: cualquier cuenta con sesión ve tu perfil completo; false: solo tu club y tu plantilla.")]
    bool Public) : ICommand<PlayerDto>;

internal sealed class SetMyPublicProfileHandler(
    ICurrentUser currentUser,
    IPlayerStore players,
    ISquadStore squads,
    ITeamStore teams,
    ICompetitionStore competitions,
    IUnitOfWork unitOfWork) : ICommandHandler<SetMyPublicProfileCommand, PlayerDto>
{
    public async Task<PlayerDto> Handle(SetMyPublicProfileCommand command, CancellationToken cancellationToken)
    {
        var player = await Resolvers.CurrentPlayerAsync(currentUser, players, cancellationToken);
        player.SetPublicProfile(command.Public);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await PlayerMapper.ToDtoAsync(player, squads, teams, competitions, cancellationToken);
    }
}
