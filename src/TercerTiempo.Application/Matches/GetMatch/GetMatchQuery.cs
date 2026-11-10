using TercerTiempo.Application.Squads;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Application.Passes;
using TercerTiempo.Application.Performances;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Licensing;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Passes;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Videos;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Matches.GetMatch;

public sealed record GetMatchQuery(Guid MatchId) : IQuery<MatchDetailDto>;

internal sealed class GetMatchHandler(
    ICurrentUser currentUser,
    IMatchStore matches,
    ITeamStore teams,
    ISquadStore squads,
    IPerformanceStore performances,
    IClubStore clubs,
    ICompetitionStore competitions,
    ILicenseGate licenses,
    IMatchPassStore passes,
    IMatchPassRequestStore requests,
    IMatchPassPriceStore prices,
    IMatchVideoStore videos,
    IOptions<PassOptions> passOptions,
    TimeProvider clock) : IQueryHandler<GetMatchQuery, MatchDetailDto>
{
    public async Task<MatchDetailDto> Handle(GetMatchQuery query, CancellationToken cancellationToken)
    {
        var match = await matches.GetByIdAsync(query.MatchId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.MatchNotFound, query.MatchId);

        var teamMap = (await teams.GetByIdsAsync([match.HomeTeamId, match.AwayTeamId], cancellationToken))
            .ToDictionary(t => t.Id);

        var matchSquads = await squads.GetByTeamIdsAsync([match.HomeTeamId, match.AwayTeamId], match.SeasonId, cancellationToken);
        var sheets = new List<MatchSquadSheetDto>();

        var now = clock.GetUtcNow();
        var insight = await PassAccess.AllAsync(currentUser, passes, cancellationToken);
        var matchVideos = await videos.GetByMatchAsync(match.Id, cancellationToken);
        var myRequests = currentUser.UserId is { } requesterId
            ? (await requests.GetByRequesterAsync(requesterId, cancellationToken)).Where(r => r.MatchId == match.Id).ToList()
            : [];

        foreach (var squad in matchSquads.OrderBy(s => s.TeamId == match.HomeTeamId ? 0 : 1))
        {
            // Lo interno solo lo ven los de dentro con licencia y quien tenga un pase del acta de este partido. Que la
            // plantilla sea pública no enseña nada: solo permite pedir el acceso al club.
            var insider = await SquadAccess.IsInsiderAsync(squad, currentUser, clock, clubs, cancellationToken);
            var pass = insight.GetValueOrDefault((squad.Id, match.Id), MatchInsight.None);
            var teamLicensed = await licenses.StatusForTeamAsync(squad.TeamId, cancellationToken) != LicenseStatus.Expired;
            var visible = insider
                ? await licenses.CanAccessSquadAsync(squad, currentUser, cancellationToken)
                : pass.Sheet && teamLicensed;
            var entries = new List<PerformanceDto>();

            if (visible)
            {
                var playerMap = squad.Players.ToDictionary(p => p.Id);
                var photos = await SquadMapper.PhotoLinksAsync(squad, teams, cancellationToken);
                entries = (await performances.GetBySquadAndMatchAsync(squad.Id, match.Id, cancellationToken))
                    .Select(p => PerformanceMapper.ToDto(p, playerMap.GetValueOrDefault(p.SquadPlayerId)) with
                    {
                        PlayerPhotoUrl = playerMap.GetValueOrDefault(p.SquadPlayerId)?.RosterEntryId is { } rosterId ? photos.GetValueOrDefault(rosterId) : null,
                    })
                    .OrderByDescending(p => p.Started)
                    .ThenByDescending(p => p.MinutesPlayed)
                    .ThenBy(p => p.ShirtNumber ?? int.MaxValue)
                    .ToList();
            }

            var access = insider
                ? null
                : await AccessAsync(squad, match, pass, teamLicensed, matchVideos, myRequests, now, cancellationToken);

            sheets.Add(new MatchSquadSheetDto(
                squad.Id, squad.TeamId, teamMap.GetValueOrDefault(squad.TeamId)?.Name ?? "Equipo",
                squad.Visibility, visible, insider, entries, access));
        }

        // Quien gestiona uno de los dos equipos (su club o su cuerpo técnico vigente) lo ve como el cuerpo técnico,
        // siempre que tenga la licencia vigente.
        var club = currentUser.ClubId is { } clubId ? await clubs.GetByIdAsync(clubId, cancellationToken) : null;
        var visibleSquads = sheets.Where(s => s.Visible && s.Insider).Select(s => s.SquadId).ToHashSet();
        var managedSquad = matchSquads.FirstOrDefault(s => visibleSquads.Contains(s.Id)
            && (SquadAccess.IsStaff(s, currentUser, clock) || club?.Manages(s.TeamId) == true));
        Guid? managedTeamId = managedSquad?.TeamId;
        if (managedTeamId is null && club is not null && await licenses.IsViewerLicensedAsync(currentUser, cancellationToken))
            managedTeamId = new[] { match.HomeTeamId, match.AwayTeamId }
                .Where(id => club.Manages(id) && matchSquads.All(s => s.TeamId != id))
                .Select(id => (Guid?)id)
                .FirstOrDefault();

        var competition = await competitions.GetBySeasonAsync(match.SeasonId, cancellationToken);
        var season = competition?.Seasons.FirstOrDefault(s => s.Id == match.SeasonId);
        return new MatchDetailDto(MatchMapper.ToDto(match, teamMap), competition?.Name, season?.Label, sheets, managedTeamId, managedSquad?.Id);
    }

    // Lo que ve alguien de fuera: qué le abren sus pases y si puede pedir el acceso (y a qué precio).
    private async Task<MatchSheetAccessDto> AccessAsync(
        Squad squad, Match match, MatchInsight pass, bool teamLicensed, IReadOnlyList<MatchVideo> matchVideos,
        IReadOnlyList<MatchPassRequest> myRequests, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var hasVideo = matchVideos.Any(v => v.SquadId == squad.Id && !v.IsDeleted);
        var club = squad.Visibility == SquadVisibility.Public && teamLicensed && match.IsOver(now)
            ? await clubs.GetByTeamAsync(squad.TeamId, cancellationToken)
            : null;
        var (sheetPrice, videoPrice) = club is null ? (0, 0) : await PassContext.PricesAsync(club, squad.Id, match.Id, prices, cancellationToken);

        var request = myRequests.Where(r => r.SquadId == squad.Id).OrderByDescending(r => r.CreatedAt).FirstOrDefault();
        MatchPass? ready = null;
        if (currentUser.UserId is { } userId)
            ready = (await passes.GetBySquadMatchAsync(squad.Id, match.Id, cancellationToken))
                .Where(p => p.ForUserId == userId && p.StatusOn(now) == MatchPassStatus.Pending)
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefault();

        return new MatchSheetAccessDto(
            pass.Sheet, pass.Video, pass.Download, club is not null, hasVideo, sheetPrice, videoPrice, passOptions.Value.Currency,
            request?.Status, ready?.Code, ready?.PriceCents);
    }
}
