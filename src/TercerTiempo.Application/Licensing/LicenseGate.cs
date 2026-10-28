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
    ILicensingCatalog catalog,
    TimeProvider clock) : ILicenseGate
{
    private readonly Dictionary<Guid, LicenseStatus> _statusCache = [];
    private bool? _viewerLicensed;

    public async Task<ClubLicense> EnsureForClubAsync(Club club, CancellationToken cancellationToken)
    {
        if (await licenses.GetByClubAsync(club.Id, cancellationToken) is { } existing)
            return existing;

        // La prueba cuenta desde que el club vinculó su primer equipo, no desde la primera consulta.
        var startedAt = club.Teams.Count > 0 ? club.Teams.Min(t => t.AddedAt) : club.RegisteredAt;
        var trialDays = (await catalog.GetAsync(cancellationToken)).TrialDays;
        var license = ClubLicense.Start(club.Id, DateOnly.FromDateTime(startedAt.UtcDateTime), trialDays, clock.GetUtcNow());
        licenses.Add(license);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return license;
    }

    // Un equipo está cubierto por la licencia del club que lo tiene vinculado; sin club, no tiene licencia.
    public async Task<LicenseStatus> StatusForTeamAsync(Guid teamId, CancellationToken cancellationToken)
    {
        if (_statusCache.TryGetValue(teamId, out var cached)) return cached;

        var club = await clubs.GetByTeamAsync(teamId, cancellationToken);
        var license = club is null ? null : await EnsureForClubAsync(club, cancellationToken);
        var status = license?.StatusOn(SquadAccess.Today(clock)) ?? LicenseStatus.Expired;
        _statusCache[teamId] = status;
        return status;
    }

    // El administrador mira sin licencia propia (no es de ningún club) y sin tocar las de los equipos que mira.
    public async Task<bool> IsViewerLicensedAsync(ICurrentUser user, CancellationToken cancellationToken)
    {
        if (user.IsAdmin) return true;
        if (_viewerLicensed is { } known) return known;
        _viewerLicensed = await ComputeViewerLicensedAsync(user, cancellationToken);
        return _viewerLicensed.Value;
    }

    private async Task<bool> ComputeViewerLicensedAsync(ICurrentUser user, CancellationToken cancellationToken)
    {
        if (!user.IsAuthenticated) return false;
        var today = SquadAccess.Today(clock);

        if (user.ClubId is { } clubId && await clubs.GetByIdAsync(clubId, cancellationToken) is { } club)
            return (await EnsureForClubAsync(club, cancellationToken)).IsActiveOn(today);

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
        if (user.IsAdmin) return true;
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
        if (user.IsAdmin) return;
        if (await StatusForTeamAsync(squad.TeamId, cancellationToken) == LicenseStatus.Expired)
            throw new ForbiddenException(ErrorCode.LicenseRequired);

        if (!await SquadAccess.IsInsiderAsync(squad, user, clock, clubs, cancellationToken) && !await IsViewerLicensedAsync(user, cancellationToken))
            throw new ForbiddenException(ErrorCode.LicenseViewerRequired);
    }

    public async Task EnsureVideoAccessAsync(Guid videoId, ICurrentUser user, CancellationToken cancellationToken)
    {
        var video = await videos.GetByIdAsync(videoId, cancellationToken);
        if (video is null) return;
        // Un vídeo público lo ve cualquiera que llegue al partido, con o sin cuenta (como un enlace compartido): basta la
        // licencia del equipo que lo subió.
        if (video.IsPublic && !user.IsAdmin)
        {
            if (await StatusForTeamAsync(video.TeamId, cancellationToken) == LicenseStatus.Expired)
                throw new ForbiddenException(ErrorCode.LicenseRequired);
            return;
        }
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
