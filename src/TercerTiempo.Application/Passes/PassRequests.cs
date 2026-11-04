using FluentValidation;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Application.Matches;
using TercerTiempo.Domain.Alerts;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Licensing;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Passes;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Application.Passes;

public sealed record CreatePassRequestCommand(Guid SquadId, Guid MatchId, bool WantsSheet, bool WantsVideo, string? Message) : ICommand<MyPassRequestDto>;

public sealed record CancelPassRequestCommand(Guid RequestId) : ICommand<bool>;

public sealed record ListClubPassRequestsQuery : IQuery<IReadOnlyList<ClubPassRequestDto>>;

public sealed record ApprovePassRequestCommand(Guid RequestId, bool IncludesSheet, bool IncludesVideo, bool AllowsDownload, int? PriceCents) : ICommand<ClubPassRequestDto>;

public sealed record RejectPassRequestCommand(Guid RequestId) : ICommand<ClubPassRequestDto>;

internal sealed class CreatePassRequestValidator : AbstractValidator<CreatePassRequestCommand>
{
    public CreatePassRequestValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.MatchId).NotEmpty();
        RuleFor(x => x.Message).MaximumLength(300);
    }
}

internal sealed class ApprovePassRequestValidator : AbstractValidator<ApprovePassRequestCommand>
{
    public ApprovePassRequestValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.PriceCents).InclusiveBetween(0, MatchPass.MaxPriceCents).When(x => x.PriceCents.HasValue);
    }
}

internal static class PassRequestAlerts
{
    public static string Key(Guid requestId) => $"pass-request:{requestId}";

    public static async Task ResolveAsync(MatchPassRequest request, IAlertStore alerts, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var key = Key(request.Id);
        foreach (var alert in (await alerts.GetByClubAsync(request.ClubId, cancellationToken)).Where(a => a.Key == key))
            alert.Resolve(now);
    }

    public static string Contents(bool sheet, bool video) =>
        sheet && video ? "el acta interna y el vídeo" : sheet ? "el acta interna" : "el vídeo";
}

internal sealed class CreatePassRequestHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IClubStore clubs,
    ICoachStore coaches,
    IPlayerStore players,
    IMatchStore matches,
    ITeamStore teams,
    IMatchVideoStore videos,
    IMatchPassRequestStore requests,
    IAlertStore alerts,
    ILicenseGate licenses,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<CreatePassRequestCommand, MyPassRequestDto>
{
    public async Task<MyPassRequestDto> Handle(CreatePassRequestCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId) throw new UnauthenticatedException();

        var squad = await squads.GetByIdAsync(command.SquadId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.SquadNotFound, command.SquadId);

        if (await SquadAccess.IsInsiderAsync(squad, currentUser, clock, clubs, cancellationToken))
            throw new ConflictException(ErrorCode.PassOwnTeam);

        if (squad.Visibility != SquadVisibility.Public)
            throw new ForbiddenException(ErrorCode.PassRequestSquadPrivate);

        if (await licenses.StatusForTeamAsync(squad.TeamId, cancellationToken) == LicenseStatus.Expired)
            throw new ForbiddenException(ErrorCode.LicenseRequired);

        var club = await PassContext.OwnerClubAsync(squad, clubs, cancellationToken);
        var match = await PassContext.PlayedMatchOfSquadAsync(squad, command.MatchId, matches, clock, cancellationToken);

        if (command.WantsVideo && !(await videos.GetByMatchAsync(match.Id, cancellationToken)).Any(v => v.SquadId == squad.Id))
            throw new ConflictException(ErrorCode.PassNoVideo);

        if ((await requests.GetByRequesterAsync(userId, cancellationToken)).Any(r => r.IsPending && r.SquadId == squad.Id && r.MatchId == match.Id))
            throw new ConflictException(ErrorCode.PassRequestDuplicate);

        var now = clock.GetUtcNow();
        var name = await PassContext.AccountNameAsync(currentUser, clubs, coaches, players, cancellationToken);
        var request = MatchPassRequest.Create(
            squad.Id, squad.TeamId, match.Id, club.Id, userId, name, command.WantsSheet, command.WantsVideo, command.Message, now);
        requests.Add(request);

        var teamMap = await PassContext.TeamsOfAsync([match], teams, cancellationToken);
        var fixture = $"{teamMap.GetValueOrDefault(match.HomeTeamId)?.Name} - {teamMap.GetValueOrDefault(match.AwayTeamId)?.Name}";
        alerts.Add(Alert.Raise(
            club.Id, AlertKind.PassRequest, PassRequestAlerts.Key(request.Id), "Solicitud de acceso",
            $"{name} pide {PassRequestAlerts.Contents(request.WantsSheet, request.WantsVideo)} del {fixture}.",
            "/club/pases", null, now));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new MyPassRequestDto(
            request.Id, squad.Id, squad.TeamId, teamMap.GetValueOrDefault(squad.TeamId)?.Name ?? "Equipo", MatchMapper.ToDto(match, teamMap),
            request.WantsSheet, request.WantsVideo, request.Message, request.Status, request.CreatedAt, null, null, null, false, false, false);
    }
}

internal sealed class CancelPassRequestHandler(
    ICurrentUser currentUser,
    IMatchPassRequestStore requests,
    IAlertStore alerts,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<CancelPassRequestCommand, bool>
{
    public async Task<bool> Handle(CancelPassRequestCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId) throw new UnauthenticatedException();

        var request = await requests.GetByIdAsync(command.RequestId, cancellationToken);
        if (request is null || request.RequesterUserId != userId)
            throw new NotFoundException(ErrorCode.PassRequestNotFound, command.RequestId);

        var now = clock.GetUtcNow();
        request.Cancel(now);
        await PassRequestAlerts.ResolveAsync(request, alerts, now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}

internal static class ClubPassRequests
{
    public static async Task<IReadOnlyList<ClubPassRequestDto>> MapAsync(
        Club club, IReadOnlyList<MatchPassRequest> list, IMatchStore matches, ITeamStore teams, IMatchVideoStore videos,
        IMatchPassStore passes, IMatchPassPriceStore prices, string currency, CancellationToken cancellationToken)
    {
        if (list.Count == 0) return [];

        var matchMap = (await matches.GetByIdsAsync(list.Select(r => r.MatchId), cancellationToken)).ToDictionary(m => m.Id);
        var teamMap = await PassContext.TeamsOfAsync(matchMap.Values, teams, cancellationToken);
        var videoKeys = (await videos.GetBySquadsAsync(list.Select(r => r.SquadId), cancellationToken))
            .Select(v => (v.SquadId, v.MatchId))
            .ToHashSet();
        var passMap = (await passes.GetByIdsAsync(list.Where(r => r.PassId != null).Select(r => r.PassId!.Value), cancellationToken))
            .ToDictionary(p => p.Id);

        var result = new List<ClubPassRequestDto>();
        foreach (var request in list.Where(r => matchMap.ContainsKey(r.MatchId)))
        {
            var price = await PassContext.PricesAsync(club, request.SquadId, request.MatchId, prices, cancellationToken);
            var pass = request.PassId is { } id ? passMap.GetValueOrDefault(id) : null;
            result.Add(new ClubPassRequestDto(
                request.Id, request.SquadId, request.TeamId, teamMap.GetValueOrDefault(request.TeamId)?.Name ?? "Equipo",
                MatchMapper.ToDto(matchMap[request.MatchId], teamMap), request.RequesterName, request.WantsSheet, request.WantsVideo,
                request.Message, request.Status, request.CreatedAt, request.DecidedAt, videoKeys.Contains((request.SquadId, request.MatchId)),
                price.Sheet, price.Video, currency, pass?.Code, pass?.IsRedeemed ?? false));
        }

        return result;
    }

    public static async Task<(Club Club, MatchPassRequest Request)> OwnedAsync(
        Guid requestId, ICurrentUser currentUser, IClubStore clubs, IMatchPassRequestStore requests, CancellationToken cancellationToken)
    {
        var club = await Resolvers.CurrentClubAsync(currentUser, clubs, cancellationToken);
        var request = await requests.GetByIdAsync(requestId, cancellationToken);
        if (request is null || request.ClubId != club.Id)
            throw new NotFoundException(ErrorCode.PassRequestNotFound, requestId);
        return (club, request);
    }
}

internal sealed class ListClubPassRequestsHandler(
    ICurrentUser currentUser,
    IClubStore clubs,
    IMatchPassRequestStore requests,
    IMatchStore matches,
    ITeamStore teams,
    IMatchVideoStore videos,
    IMatchPassStore passes,
    IMatchPassPriceStore prices,
    IOptions<PassOptions> options) : IQueryHandler<ListClubPassRequestsQuery, IReadOnlyList<ClubPassRequestDto>>
{
    private const int DecidedShown = 50;

    public async Task<IReadOnlyList<ClubPassRequestDto>> Handle(ListClubPassRequestsQuery query, CancellationToken cancellationToken)
    {
        var club = await Resolvers.CurrentClubAsync(currentUser, clubs, cancellationToken);
        var all = await requests.GetByClubAsync(club.Id, cancellationToken);

        var shown = all.Where(r => r.IsPending).OrderBy(r => r.CreatedAt)
            .Concat(all.Where(r => !r.IsPending).OrderByDescending(r => r.DecidedAt ?? r.CreatedAt).Take(DecidedShown))
            .ToList();

        return await ClubPassRequests.MapAsync(club, shown, matches, teams, videos, passes, prices, options.Value.Currency, cancellationToken);
    }
}

internal sealed class ApprovePassRequestHandler(
    ICurrentUser currentUser,
    IClubStore clubs,
    ISquadStore squads,
    IMatchPassRequestStore requests,
    IMatchStore matches,
    ITeamStore teams,
    IMatchVideoStore videos,
    IMatchPassStore passes,
    IMatchPassPriceStore prices,
    IAlertStore alerts,
    IOptions<PassOptions> options,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<ApprovePassRequestCommand, ClubPassRequestDto>
{
    public async Task<ClubPassRequestDto> Handle(ApprovePassRequestCommand command, CancellationToken cancellationToken)
    {
        var (club, request) = await ClubPassRequests.OwnedAsync(command.RequestId, currentUser, clubs, requests, cancellationToken);
        var squad = await squads.GetByIdAsync(request.SquadId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.SquadNotFound, request.SquadId);

        if (command.IncludesVideo && !(await videos.GetByMatchAsync(request.MatchId, cancellationToken)).Any(v => v.SquadId == squad.Id))
            throw new ConflictException(ErrorCode.PassNoVideo);

        var price = command.PriceCents
            ?? PassContext.Total(await PassContext.PricesAsync(club, squad.Id, request.MatchId, prices, cancellationToken), command.IncludesSheet, command.IncludesVideo);

        var settings = options.Value;
        var now = clock.GetUtcNow();
        var pass = MatchPass.Create(
            squad.Id, squad.TeamId, request.MatchId, club.Id, command.IncludesSheet, command.IncludesVideo, command.AllowsDownload,
            price, settings.Currency, request.RequesterName, null, club.Id, now, TimeSpan.FromDays(settings.RedeemDays),
            request.RequesterUserId, request.Id);

        request.Approve(pass.Id, now);
        passes.Add(pass);
        await PassRequestAlerts.ResolveAsync(request, alerts, now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return (await ClubPassRequests.MapAsync(club, [request], matches, teams, videos, passes, prices, settings.Currency, cancellationToken))[0];
    }
}

internal sealed class RejectPassRequestHandler(
    ICurrentUser currentUser,
    IClubStore clubs,
    IMatchPassRequestStore requests,
    IMatchStore matches,
    ITeamStore teams,
    IMatchVideoStore videos,
    IMatchPassStore passes,
    IMatchPassPriceStore prices,
    IAlertStore alerts,
    IOptions<PassOptions> options,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<RejectPassRequestCommand, ClubPassRequestDto>
{
    public async Task<ClubPassRequestDto> Handle(RejectPassRequestCommand command, CancellationToken cancellationToken)
    {
        var (club, request) = await ClubPassRequests.OwnedAsync(command.RequestId, currentUser, clubs, requests, cancellationToken);

        var now = clock.GetUtcNow();
        request.Reject(now);
        await PassRequestAlerts.ResolveAsync(request, alerts, now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return (await ClubPassRequests.MapAsync(club, [request], matches, teams, videos, passes, prices, options.Value.Currency, cancellationToken))[0];
    }
}
