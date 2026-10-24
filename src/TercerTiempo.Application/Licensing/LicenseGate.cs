using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Licensing;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Application.Licensing;

internal sealed class LicenseGate(
    ILicenseStore licenses,
    IClubStore clubs,
    ISquadStore squads,
    IMatchVideoStore videos,
    IVideoShareStore shares,
    IPlayerStore players,
    IUnitOfWork unitOfWork,
    IOptions<LicensingOptions> options,
    TimeProvider clock) : ILicenseGate
{
    private readonly Dictionary<Guid, LicenseStatus> _statusCache = [];
    private bool? _viewerLicensed;

    public async Task<TeamLicense> EnsureForTeamAsync(Club club, Guid teamId, CancellationToken cancellationToken)
    {
        if (await licenses.GetByTeamAsync(teamId, cancellationToken) is { } existing)
            return existing;

        // La prueba cuenta desde que el club vinculó el equipo, no desde la primera consulta.
        var linkedOn = club.Teams.FirstOrDefault(t => t.TeamId == teamId)?.AddedAt ?? clock.GetUtcNow();
        var license = TeamLicense.Start(club.Id, teamId, DateOnly.FromDateTime(linkedOn.UtcDateTime), options.Value.TrialDays, clock.GetUtcNow());
        licenses.Add(license);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        _statusCache[teamId] = license.StatusOn(SquadAccess.Today(clock));
        return license;
    }

    public async Task<LicenseStatus> StatusForTeamAsync(Guid teamId, CancellationToken cancellationToken)
    {
        if (_statusCache.TryGetValue(teamId, out var cached)) return cached;

        var today = SquadAccess.Today(clock);
        var license = await licenses.GetByTeamAsync(teamId, cancellationToken);
        if (license is null && await clubs.GetByTeamAsync(teamId, cancellationToken) is { } club)
            license = await EnsureForTeamAsync(club, teamId, cancellationToken);

        var status = license?.StatusOn(today) ?? LicenseStatus.Expired;
        _statusCache[teamId] = status;
        return status;
    }

    public async Task<bool> IsViewerLicensedAsync(ICurrentUser user, CancellationToken cancellationToken)
    {
        if (_viewerLicensed is { } known) return known;
        _viewerLicensed = await ComputeViewerLicensedAsync(user, cancellationToken);
        return _viewerLicensed.Value;
    }

    private async Task<bool> ComputeViewerLicensedAsync(ICurrentUser user, CancellationToken cancellationToken)
    {
        if (!user.IsAuthenticated) return false;
        var today = SquadAccess.Today(clock);

        if (user.ClubId is { } clubId && await clubs.GetByIdAsync(clubId, cancellationToken) is { } club)
        {
            foreach (var team in club.Teams)
                if (await StatusForTeamAsync(team.TeamId, cancellationToken) != LicenseStatus.Expired) return true;
            return false;
        }

        if (user.CoachId is { } coachId)
        {
            foreach (var squad in await squads.GetByCoachAsync(coachId, today, cancellationToken))
                if (await StatusForTeamAsync(squad.TeamId, cancellationToken) != LicenseStatus.Expired) return true;
            return false;
        }

        if (user.PlayerId is { } playerId && await squads.GetByPlayerAsync(playerId, cancellationToken) is { } own)
            return await StatusForTeamAsync(own.TeamId, cancellationToken) != LicenseStatus.Expired;

        return false;
    }

    public async Task<bool> CanAccessSquadAsync(Squad squad, ICurrentUser user, CancellationToken cancellationToken)
    {
        if (await StatusForTeamAsync(squad.TeamId, cancellationToken) == LicenseStatus.Expired) return false;
        if (await SquadAccess.IsInsiderAsync(squad, user, clock, clubs, cancellationToken)) return true;
        return await IsViewerLicensedAsync(user, cancellationToken);
    }

    public async Task EnsureSquadAccessAsync(Guid squadId, ICurrentUser user, CancellationToken cancellationToken)
    {
        var squad = await squads.GetByIdAsync(squadId, cancellationToken);
        if (squad is null) return;
        await EnsureAsync(squad, user, cancellationToken);
    }

    private async Task EnsureAsync(Squad squad, ICurrentUser user, CancellationToken cancellationToken)
    {
        if (await StatusForTeamAsync(squad.TeamId, cancellationToken) == LicenseStatus.Expired)
            throw new ForbiddenException(ErrorCode.LicenseRequired);

        if (!await SquadAccess.IsInsiderAsync(squad, user, clock, clubs, cancellationToken) && !await IsViewerLicensedAsync(user, cancellationToken))
            throw new ForbiddenException(ErrorCode.LicenseViewerRequired);
    }

    public async Task EnsureVideoAccessAsync(Guid videoId, ICurrentUser user, CancellationToken cancellationToken)
    {
        var video = await videos.GetByIdAsync(videoId, cancellationToken);
        if (video is null) return;
        var squad = await squads.GetByIdAsync(video.SquadId, cancellationToken);
        if (squad is null) return;
        await EnsureAsync(squad, user, cancellationToken);
    }

    public async Task EnsureSharedVideoAccessAsync(string token, CancellationToken cancellationToken)
    {
        var link = await shares.GetByTokenAsync(token, cancellationToken);
        if (link is null) return;
        var video = await videos.GetByIdAsync(link.VideoId, cancellationToken);
        if (video is null) return;
        if (await StatusForTeamAsync(video.TeamId, cancellationToken) == LicenseStatus.Expired)
            throw new ForbiddenException(ErrorCode.LicenseRequired);
    }

    public async Task EnsureTeamLicensedAsync(Guid teamId, CancellationToken cancellationToken)
    {
        if (await StatusForTeamAsync(teamId, cancellationToken) == LicenseStatus.Expired)
            throw new ForbiddenException(ErrorCode.LicenseRequired);
    }

    public async Task EnsurePlayerAccessAsync(ICurrentUser user, CancellationToken cancellationToken)
    {
        if (!user.IsAuthenticated || user.UserId is not { } userId) return;
        var player = await players.GetByUserIdAsync(userId, cancellationToken);
        if (player is null) return;
        var squad = await squads.GetByPlayerAsync(player.Id, cancellationToken);
        if (squad is null) return;
        if (await StatusForTeamAsync(squad.TeamId, cancellationToken) == LicenseStatus.Expired)
            throw new ForbiddenException(ErrorCode.LicenseRequired);
    }
}
