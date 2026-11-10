using FluentValidation;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Application.Matches;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Licensing;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Passes;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Application.Passes;

public sealed record ListMatchPassesQuery(Guid SquadId, Guid MatchId) : IQuery<MatchPassesDto>;

public sealed record CreateMatchPassCommand(
    Guid SquadId,
    Guid MatchId,
    bool IncludesSheet,
    bool IncludesVideo,
    bool AllowsDownload,
    int? PriceCents,
    string? Recipient) : ICommand<MatchPassDto>;

public sealed record RevokeMatchPassCommand(Guid SquadId, Guid MatchId, Guid PassId) : ICommand<MatchPassesDto>;

public sealed record GetPassByCodeQuery(string Code) : IQuery<PassPreviewDto>;

public sealed record RedeemPassCommand(string Code) : ICommand<MyPassDto>;

public sealed record ListMyPassesQuery : IQuery<MyPassesDto>;

internal sealed class CreateMatchPassValidator : AbstractValidator<CreateMatchPassCommand>
{
    public CreateMatchPassValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.MatchId).NotEmpty();
        RuleFor(x => x.PriceCents).InclusiveBetween(0, MatchPass.MaxPriceCents).When(x => x.PriceCents.HasValue);
        RuleFor(x => x.Recipient).MaximumLength(100);
    }
}

internal sealed class GetPassByCodeValidator : AbstractValidator<GetPassByCodeQuery>
{
    public GetPassByCodeValidator() => RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
}

internal sealed class RedeemPassValidator : AbstractValidator<RedeemPassCommand>
{
    public RedeemPassValidator() => RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
}

internal static class MatchPasses
{
    public static async Task<MatchPassesDto> ListAsync(
        Squad squad, Club club, Match match, PassOptions options, IMatchPassStore passes, IMatchPassPriceStore prices, IMatchVideoStore videos,
        IPerformanceStore performances, TimeProvider clock, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var price = await PassContext.PricesAsync(club, squad.Id, match.Id, prices, cancellationToken);
        var hasVideo = (await videos.GetByMatchAsync(match.Id, cancellationToken)).Any(v => v.SquadId == squad.Id);
        var hasSheet = (await performances.GetBySquadAndMatchAsync(squad.Id, match.Id, cancellationToken)).Count > 0;
        var list = (await passes.GetBySquadMatchAsync(squad.Id, match.Id, cancellationToken))
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => PassContext.ToDto(p, now))
            .ToList();

        return new MatchPassesDto(squad.Id, match.Id, price.Sheet, price.Video, options.Currency, options.ClubSharePercent,
            hasSheet, hasVideo, squad.Visibility == SquadVisibility.Public, list);
    }
}

internal sealed class ListMatchPassesHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IClubStore clubs,
    IMatchStore matches,
    IMatchPassStore passes,
    IMatchPassPriceStore prices,
    IMatchVideoStore videos,
    IPerformanceStore performances,
    IOptions<PassOptions> options,
    TimeProvider clock) : IQueryHandler<ListMatchPassesQuery, MatchPassesDto>
{
    public async Task<MatchPassesDto> Handle(ListMatchPassesQuery query, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, query.SquadId, clock, cancellationToken);
        var club = await PassContext.OwnerClubAsync(squad, clubs, cancellationToken);
        var match = await PassContext.PlayedMatchOfSquadAsync(squad, query.MatchId, matches, clock, cancellationToken);
        return await MatchPasses.ListAsync(squad, club, match, options.Value, passes, prices, videos, performances, clock, cancellationToken);
    }
}

internal sealed class CreateMatchPassHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IClubStore clubs,
    IMatchStore matches,
    IMatchPassStore passes,
    IMatchPassPriceStore prices,
    IMatchVideoStore videos,
    IOptions<PassOptions> options,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<CreateMatchPassCommand, MatchPassDto>
{
    public async Task<MatchPassDto> Handle(CreateMatchPassCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var club = await PassContext.OwnerClubAsync(squad, clubs, cancellationToken);
        var match = await PassContext.PlayedMatchOfSquadAsync(squad, command.MatchId, matches, clock, cancellationToken);

        if (command.IncludesVideo && !(await videos.GetByMatchAsync(match.Id, cancellationToken)).Any(v => v.SquadId == squad.Id))
            throw new ConflictException(ErrorCode.PassNoVideo);

        var price = command.PriceCents
            ?? PassContext.Total(await PassContext.PricesAsync(club, squad.Id, match.Id, prices, cancellationToken), command.IncludesSheet, command.IncludesVideo);

        var settings = options.Value;
        var now = clock.GetUtcNow();
        var pass = MatchPass.Create(
            squad.Id, squad.TeamId, match.Id, club.Id, command.IncludesSheet, command.IncludesVideo, command.AllowsDownload,
            price, settings.Currency, command.Recipient, currentUser.ClubId is null ? currentUser.CoachId : null,
            currentUser.ClubId, now, TimeSpan.FromDays(settings.RedeemDays));

        passes.Add(pass);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return PassContext.ToDto(pass, now);
    }
}

internal sealed class RevokeMatchPassHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IClubStore clubs,
    IMatchStore matches,
    IMatchPassStore passes,
    IMatchPassPriceStore prices,
    IMatchVideoStore videos,
    IPerformanceStore performances,
    IOptions<PassOptions> options,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<RevokeMatchPassCommand, MatchPassesDto>
{
    public async Task<MatchPassesDto> Handle(RevokeMatchPassCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var club = await PassContext.OwnerClubAsync(squad, clubs, cancellationToken);
        var match = await PassContext.PlayedMatchOfSquadAsync(squad, command.MatchId, matches, clock, cancellationToken);

        var pass = await passes.GetByIdAsync(command.PassId, cancellationToken);
        if (pass is null || pass.SquadId != squad.Id || pass.MatchId != match.Id)
            throw new NotFoundException(ErrorCode.PassNotFound);

        pass.Revoke(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await MatchPasses.ListAsync(squad, club, match, options.Value, passes, prices, videos, performances, clock, cancellationToken);
    }
}

internal sealed class GetPassByCodeHandler(
    ICurrentUser currentUser,
    IMatchPassStore passes,
    IMatchStore matches,
    ITeamStore teams,
    IClubStore clubs,
    TimeProvider clock) : IQueryHandler<GetPassByCodeQuery, PassPreviewDto>
{
    public async Task<PassPreviewDto> Handle(GetPassByCodeQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId) throw new UnauthenticatedException();

        var pass = await passes.GetByCodeAsync(query.Code, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.PassNotFound);
        var match = await matches.GetByIdAsync(pass.MatchId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.MatchNotFound, pass.MatchId);
        var teamMap = await PassContext.TeamsOfAsync([match], teams, cancellationToken);
        var club = await clubs.GetByIdAsync(pass.ClubId, cancellationToken);

        var mine = pass.RedeemedByUserId == userId;
        var someoneElse = (pass.IsRedeemed && !mine) || (pass.ForUserId is { } intended && intended != userId);

        return new PassPreviewDto(
            pass.Code, pass.StatusOn(clock.GetUtcNow()), pass.SquadId, pass.TeamId, teamMap.GetValueOrDefault(pass.TeamId)?.Name ?? "Equipo",
            club?.Name ?? "Club", MatchMapper.ToDto(match, teamMap), pass.IncludesSheet, pass.IncludesVideo, pass.AllowsDownload,
            pass.PriceCents, pass.Currency, pass.ExpiresAt, mine, someoneElse);
    }
}

// Canjear: el pase se reserva primero (la versión de la fila impide que dos personas se lo queden a la vez), luego se
// cobra si tiene precio y, si el cobro no sale, vuelve a quedar libre.
internal sealed class RedeemPassHandler(
    ICurrentUser currentUser,
    IMatchPassStore passes,
    IPassSaleStore sales,
    ISquadStore squads,
    IClubStore clubs,
    ICoachStore coaches,
    IPlayerStore players,
    IMatchStore matches,
    ITeamStore teams,
    ILicenseGate licenses,
    IPaymentGateway payments,
    IOptions<PassOptions> options,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<RedeemPassCommand, MyPassDto>
{
    public async Task<MyPassDto> Handle(RedeemPassCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId) throw new UnauthenticatedException();

        var pass = await passes.GetByCodeAsync(command.Code, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.PassNotFound);
        var squad = await squads.GetByIdAsync(pass.SquadId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.SquadNotFound, pass.SquadId);

        if (await SquadAccess.IsInsiderAsync(squad, currentUser, clock, clubs, cancellationToken))
            throw new ConflictException(ErrorCode.PassOwnTeam);

        if (await licenses.StatusForTeamAsync(pass.TeamId, cancellationToken) == LicenseStatus.Expired)
            throw new ForbiddenException(ErrorCode.LicenseRequired);

        var match = await matches.GetByIdAsync(pass.MatchId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.MatchNotFound, pass.MatchId);
        var teamMap = await PassContext.TeamsOfAsync([match], teams, cancellationToken);

        var now = clock.GetUtcNow();
        pass.Redeem(userId, await PassContext.AccountNameAsync(currentUser, clubs, coaches, players, cancellationToken), now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (!pass.IsFree)
            await ChargeAsync(pass, userId, match, teamMap, now, cancellationToken);

        return PassContext.ToMine(pass, new Dictionary<Guid, Match> { [match.Id] = match }, teamMap);
    }

    private async Task ChargeAsync(MatchPass pass, Guid userId, Match match, IReadOnlyDictionary<Guid, Team> teamMap, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var contents = pass.IncludesSheet && pass.IncludesVideo ? "acta interna y vídeo" : pass.IncludesSheet ? "acta interna" : "vídeo";
        var description = $"Pase del {teamMap.GetValueOrDefault(match.HomeTeamId)?.Name} - {teamMap.GetValueOrDefault(match.AwayTeamId)?.Name} ({contents})";

        PaymentResult payment;
        try
        {
            payment = await payments.ChargeAsync(new PaymentRequest(userId, pass.PriceCents, pass.Currency, description), cancellationToken);
        }
        catch
        {
            await ReleaseAsync(pass);
            throw;
        }

        if (!payment.Succeeded)
        {
            await ReleaseAsync(pass);
            throw payment.Error is { } reason
                ? new ConflictException(ErrorCode.PassPaymentRejected, reason)
                : new ConflictException(ErrorCode.PassPaymentFailed);
        }

        sales.Add(PassSale.Record(pass, userId, options.Value.ClubSharePercent, payments.ProviderName, payment.Reference, now));
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
    }

    private async Task ReleaseAsync(MatchPass pass)
    {
        pass.ReleaseRedemption();
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
    }
}

internal sealed class ListMyPassesHandler(
    ICurrentUser currentUser,
    IMatchPassStore passes,
    IMatchPassRequestStore requests,
    IMatchStore matches,
    ITeamStore teams) : IQueryHandler<ListMyPassesQuery, MyPassesDto>
{
    public async Task<MyPassesDto> Handle(ListMyPassesQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId) throw new UnauthenticatedException();

        var mine = (await passes.GetRedeemedByAsync(userId, cancellationToken)).Where(p => p.Grants(userId)).ToList();
        var asked = await requests.GetByRequesterAsync(userId, cancellationToken);
        var approved = (await passes.GetByIdsAsync(asked.Where(r => r.PassId != null).Select(r => r.PassId!.Value), cancellationToken))
            .ToDictionary(p => p.Id);

        var matchMap = (await matches.GetByIdsAsync(mine.Select(p => p.MatchId).Concat(asked.Select(r => r.MatchId)), cancellationToken))
            .ToDictionary(m => m.Id);
        var teamMap = await PassContext.TeamsOfAsync(matchMap.Values, teams, cancellationToken);

        var passList = mine
            .Where(p => matchMap.ContainsKey(p.MatchId))
            .OrderByDescending(p => matchMap[p.MatchId].KickoffAt)
            .Select(p => PassContext.ToMine(p, matchMap, teamMap))
            .ToList();

        var requestList = asked
            .Where(r => matchMap.ContainsKey(r.MatchId))
            .OrderBy(r => r.IsPending ? 0 : 1)
            .ThenByDescending(r => r.CreatedAt)
            .Select(r =>
            {
                var pass = r.PassId is { } id ? approved.GetValueOrDefault(id) : null;
                var open = pass is not null && !pass.IsRedeemed && !pass.IsRevoked;
                return new MyPassRequestDto(
                    r.Id, r.SquadId, r.TeamId, teamMap.GetValueOrDefault(r.TeamId)?.Name ?? "Equipo", MatchMapper.ToDto(matchMap[r.MatchId], teamMap),
                    r.WantsSheet, r.WantsVideo, r.Message, r.Status, r.CreatedAt, r.DecidedAt,
                    open ? pass!.Code : null, pass?.PriceCents, pass?.IncludesSheet ?? false, pass?.IncludesVideo ?? false,
                    pass?.IsRedeemed ?? false);
            })
            .ToList();

        return new MyPassesDto(passList, requestList);
    }
}
