using System.ComponentModel;
using FluentValidation;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Matches;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Passes;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Application.Passes;

public sealed record MatchPassPriceDto(
    Guid SquadId,
    Guid MatchId,
    Guid TeamId,
    string TeamName,
    MatchDto Match,
    bool HasSheet,
    bool HasVideo,
    [property: Description("La plantilla de ese partido es pública y recibe solicitudes.")]
    bool OpenToRequests,
    [property: Description("Precio propio del partido; null = el general del club.")]
    int? SheetPriceCents,
    int? VideoPriceCents,
    int EffectiveSheetPriceCents,
    int EffectiveVideoPriceCents);

public sealed record ClubPassPricesDto(
    int SheetPriceCents,
    int VideoPriceCents,
    string Currency,
    [property: Description("Porcentaje de cada venta para el club; el resto es de la plataforma.")]
    int ClubSharePercent,
    IReadOnlyList<MatchPassPriceDto> Matches);

public sealed record PassSaleDto(
    Guid Id,
    DateTimeOffset PaidAt,
    string TeamName,
    MatchDto? Match,
    string? Buyer,
    bool IncludesSheet,
    bool IncludesVideo,
    int AmountCents,
    int ClubShareCents,
    int PlatformShareCents,
    string Currency,
    string Provider);

public sealed record PassEarningsDto(
    int SalesCount,
    int GrossCents,
    int ClubShareCents,
    int PlatformShareCents,
    string Currency,
    int ClubSharePercent,
    IReadOnlyList<PassSaleDto> Recent);

public sealed record ClubEarningsRowDto(Guid ClubId, string ClubName, int SalesCount, int GrossCents, int ClubShareCents, int PlatformShareCents);

public sealed record PlatformPassEarningsDto(
    int SalesCount,
    int GrossCents,
    int ClubShareCents,
    int PlatformShareCents,
    string Currency,
    IReadOnlyList<ClubEarningsRowDto> Clubs);

public sealed record GetClubPassPricesQuery : IQuery<ClubPassPricesDto>;

public sealed record SetClubPassPricesCommand(int SheetPriceCents, int VideoPriceCents) : ICommand<ClubPassPricesDto>;

public sealed record SetMatchPassPriceCommand(Guid SquadId, Guid MatchId, int? SheetPriceCents, int? VideoPriceCents) : ICommand<MatchPassPriceDto>;

public sealed record GetClubPassEarningsQuery : IQuery<PassEarningsDto>;

public sealed record GetPlatformPassEarningsQuery : IQuery<PlatformPassEarningsDto>;

internal sealed class SetClubPassPricesValidator : AbstractValidator<SetClubPassPricesCommand>
{
    public SetClubPassPricesValidator()
    {
        RuleFor(x => x.SheetPriceCents).InclusiveBetween(0, MatchPass.MaxPriceCents);
        RuleFor(x => x.VideoPriceCents).InclusiveBetween(0, MatchPass.MaxPriceCents);
    }
}

internal sealed class SetMatchPassPriceValidator : AbstractValidator<SetMatchPassPriceCommand>
{
    public SetMatchPassPriceValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.MatchId).NotEmpty();
        RuleFor(x => x.SheetPriceCents).InclusiveBetween(0, MatchPass.MaxPriceCents).When(x => x.SheetPriceCents.HasValue);
        RuleFor(x => x.VideoPriceCents).InclusiveBetween(0, MatchPass.MaxPriceCents).When(x => x.VideoPriceCents.HasValue);
    }
}

// Los partidos que el club puede vender: los ya jugados de las plantillas de sus equipos, de cualquier temporada.
internal static class ClubPassPrices
{
    public static async Task<ClubPassPricesDto> BuildAsync(
        Club club, ISquadStore squads, IMatchStore matches, ITeamStore teams, IPerformanceStore performances, IMatchVideoStore videos,
        IMatchPassPriceStore prices, PassOptions settings, TimeProvider clock, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var clubSquads = await squads.GetByTeamIdsAsync(club.Teams.Select(t => t.TeamId), cancellationToken);
        var custom = (await prices.GetByClubAsync(club.Id, cancellationToken)).ToDictionary(p => (p.SquadId, p.MatchId));
        var videoKeys = (await videos.GetBySquadsAsync(clubSquads.Select(s => s.Id), cancellationToken))
            .Select(v => (v.SquadId, v.MatchId))
            .ToHashSet();

        var rows = new List<(Squad Squad, Match Match, bool HasSheet)>();
        foreach (var squad in clubSquads)
        {
            var withSheet = (await performances.GetBySquadAsync(squad.Id, cancellationToken)).Select(p => p.MatchId).ToHashSet();
            foreach (var match in await matches.GetTeamMatchesAsync(squad.SeasonId, squad.TeamId, cancellationToken))
                if (match.IsOver(now))
                    rows.Add((squad, match, withSheet.Contains(match.Id)));
        }

        var teamMap = await PassContext.TeamsOfAsync(rows.Select(r => r.Match), teams, cancellationToken);
        var list = rows
            .OrderByDescending(r => r.Match.KickoffAt)
            .Select(r => Row(club, r.Squad, r.Match, r.HasSheet, videoKeys.Contains((r.Squad.Id, r.Match.Id)), custom.GetValueOrDefault((r.Squad.Id, r.Match.Id)), teamMap))
            .ToList();

        return new ClubPassPricesDto(club.SheetPassPriceCents, club.VideoPassPriceCents, settings.Currency, settings.ClubSharePercent, list);
    }

    public static MatchPassPriceDto Row(
        Club club, Squad squad, Match match, bool hasSheet, bool hasVideo, MatchPassPrice? custom, IReadOnlyDictionary<Guid, Team> teamMap) =>
        new(squad.Id, match.Id, squad.TeamId, teamMap.GetValueOrDefault(squad.TeamId)?.Name ?? "Equipo", MatchMapper.ToDto(match, teamMap),
            hasSheet, hasVideo, squad.Visibility == SquadVisibility.Public, custom?.SheetPriceCents, custom?.VideoPriceCents,
            custom?.SheetPriceCents ?? club.SheetPassPriceCents, custom?.VideoPriceCents ?? club.VideoPassPriceCents);
}

internal sealed class GetClubPassPricesHandler(
    ICurrentUser currentUser,
    IClubStore clubs,
    ISquadStore squads,
    IMatchStore matches,
    ITeamStore teams,
    IPerformanceStore performances,
    IMatchVideoStore videos,
    IMatchPassPriceStore prices,
    IOptions<PassOptions> options,
    TimeProvider clock) : IQueryHandler<GetClubPassPricesQuery, ClubPassPricesDto>
{
    public async Task<ClubPassPricesDto> Handle(GetClubPassPricesQuery query, CancellationToken cancellationToken)
    {
        var club = await Resolvers.CurrentClubAsync(currentUser, clubs, cancellationToken);
        return await ClubPassPrices.BuildAsync(club, squads, matches, teams, performances, videos, prices, options.Value, clock, cancellationToken);
    }
}

internal sealed class SetClubPassPricesHandler(
    ICurrentUser currentUser,
    IClubStore clubs,
    ISquadStore squads,
    IMatchStore matches,
    ITeamStore teams,
    IPerformanceStore performances,
    IMatchVideoStore videos,
    IMatchPassPriceStore prices,
    IOptions<PassOptions> options,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<SetClubPassPricesCommand, ClubPassPricesDto>
{
    public async Task<ClubPassPricesDto> Handle(SetClubPassPricesCommand command, CancellationToken cancellationToken)
    {
        var club = await Resolvers.CurrentClubAsync(currentUser, clubs, cancellationToken);
        club.SetPassPrices(command.SheetPriceCents, command.VideoPriceCents);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await ClubPassPrices.BuildAsync(club, squads, matches, teams, performances, videos, prices, options.Value, clock, cancellationToken);
    }
}

internal sealed class SetMatchPassPriceHandler(
    ICurrentUser currentUser,
    IClubStore clubs,
    ISquadStore squads,
    IMatchStore matches,
    ITeamStore teams,
    IPerformanceStore performances,
    IMatchVideoStore videos,
    IMatchPassPriceStore prices,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<SetMatchPassPriceCommand, MatchPassPriceDto>
{
    public async Task<MatchPassPriceDto> Handle(SetMatchPassPriceCommand command, CancellationToken cancellationToken)
    {
        var club = await Resolvers.CurrentClubAsync(currentUser, clubs, cancellationToken);
        var squad = await squads.GetByIdAsync(command.SquadId, cancellationToken);
        if (squad is null || !club.Manages(squad.TeamId))
            throw new NotFoundException(ErrorCode.SquadNotFound, command.SquadId);

        var match = await PassContext.PlayedMatchOfSquadAsync(squad, command.MatchId, matches, clock, cancellationToken);
        var now = clock.GetUtcNow();

        // Sin precio propio no hace falta guardar nada: el partido vuelve al precio general.
        var custom = await prices.GetAsync(squad.Id, match.Id, cancellationToken);
        if (custom is null)
        {
            custom = MatchPassPrice.Create(club.Id, squad.Id, match.Id, now);
            prices.Add(custom);
        }
        custom.Set(command.SheetPriceCents, command.VideoPriceCents, now);
        if (custom.IsEmpty) prices.Remove(custom);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var hasSheet = (await performances.GetBySquadAndMatchAsync(squad.Id, match.Id, cancellationToken)).Count > 0;
        var hasVideo = (await videos.GetByMatchAsync(match.Id, cancellationToken)).Any(v => v.SquadId == squad.Id);
        var teamMap = await PassContext.TeamsOfAsync([match], teams, cancellationToken);
        return ClubPassPrices.Row(club, squad, match, hasSheet, hasVideo, custom.IsEmpty ? null : custom, teamMap);
    }
}

internal static class PassEarnings
{
    public static async Task<IReadOnlyList<PassSaleDto>> RecentAsync(
        IReadOnlyList<PassSale> sales, IMatchPassStore passes, IMatchStore matches, ITeamStore teams, CancellationToken cancellationToken)
    {
        var recent = sales.OrderByDescending(s => s.PaidAt).Take(50).ToList();
        if (recent.Count == 0) return [];

        var passMap = (await passes.GetByIdsAsync(recent.Select(s => s.PassId), cancellationToken)).ToDictionary(p => p.Id);
        var matchMap = (await matches.GetByIdsAsync(recent.Select(s => s.MatchId), cancellationToken)).ToDictionary(m => m.Id);
        var teamMap = await PassContext.TeamsOfAsync(matchMap.Values, teams, cancellationToken);

        return recent.Select(s =>
        {
            var pass = passMap.GetValueOrDefault(s.PassId);
            var match = matchMap.GetValueOrDefault(s.MatchId);
            return new PassSaleDto(
                s.Id, s.PaidAt, pass is null ? "Equipo" : teamMap.GetValueOrDefault(pass.TeamId)?.Name ?? "Equipo",
                match is null ? null : MatchMapper.ToDto(match, teamMap), pass?.RedeemedByName,
                pass?.IncludesSheet ?? false, pass?.IncludesVideo ?? false, s.AmountCents, s.ClubShareCents, s.PlatformShareCents,
                s.Currency, s.Provider);
        }).ToList();
    }
}

internal sealed class GetClubPassEarningsHandler(
    ICurrentUser currentUser,
    IClubStore clubs,
    IPassSaleStore sales,
    IMatchPassStore passes,
    IMatchStore matches,
    ITeamStore teams,
    IOptions<PassOptions> options) : IQueryHandler<GetClubPassEarningsQuery, PassEarningsDto>
{
    public async Task<PassEarningsDto> Handle(GetClubPassEarningsQuery query, CancellationToken cancellationToken)
    {
        var club = await Resolvers.CurrentClubAsync(currentUser, clubs, cancellationToken);
        var list = await sales.GetByClubAsync(club.Id, cancellationToken);

        return new PassEarningsDto(
            list.Count, list.Sum(s => s.AmountCents), list.Sum(s => s.ClubShareCents), list.Sum(s => s.PlatformShareCents),
            options.Value.Currency, options.Value.ClubSharePercent, await PassEarnings.RecentAsync(list, passes, matches, teams, cancellationToken));
    }
}

internal sealed class GetPlatformPassEarningsHandler(
    IPassSaleStore sales,
    IClubStore clubs,
    IOptions<PassOptions> options) : IQueryHandler<GetPlatformPassEarningsQuery, PlatformPassEarningsDto>
{
    public async Task<PlatformPassEarningsDto> Handle(GetPlatformPassEarningsQuery query, CancellationToken cancellationToken)
    {
        var list = await sales.GetAllAsync(cancellationToken);
        var names = (await clubs.GetAllAsync(cancellationToken)).ToDictionary(c => c.Id, c => c.Name);

        var rows = list
            .GroupBy(s => s.ClubId)
            .Select(g => new ClubEarningsRowDto(
                g.Key, names.GetValueOrDefault(g.Key) ?? "Club dado de baja", g.Count(), g.Sum(s => s.AmountCents),
                g.Sum(s => s.ClubShareCents), g.Sum(s => s.PlatformShareCents)))
            .OrderByDescending(r => r.PlatformShareCents)
            .ToList();

        return new PlatformPassEarningsDto(
            list.Count, list.Sum(s => s.AmountCents), list.Sum(s => s.ClubShareCents), list.Sum(s => s.PlatformShareCents),
            options.Value.Currency, rows);
    }
}
