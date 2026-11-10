using System.ComponentModel;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Matches;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Passes;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Passes;

public sealed class PassOptions
{
    public const string SectionName = "Passes";

    // Días que tiene la persona para canjear un código; una vez canjeado, el acceso no caduca.
    public int RedeemDays { get; set; } = 30;

    // Parte de cada venta para el club; el resto es de la plataforma por gestionarla.
    public int ClubSharePercent { get; set; } = 60;

    public string Currency { get; set; } = "EUR";
}

// Lo que los pases de una persona le abren de una plantilla en un partido.
public sealed record MatchInsight(bool Sheet, bool Video, bool Download)
{
    public static readonly MatchInsight None = new(false, false, false);
}

public sealed record MatchPassDto(
    Guid Id,
    [property: Description("Código de 12 caracteres; el enlace para canjearlo es {frontal}/pase/{code}.")]
    string Code,
    bool IncludesSheet,
    bool IncludesVideo,
    bool AllowsDownload,
    int PriceCents,
    string Currency,
    string? Recipient,
    [property: Description("Pending (sin canjear), Redeemed, Revoked o Expired (caducó sin canjearse).")]
    MatchPassStatus Status,
    bool FromRequest,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RedeemedAt,
    string? RedeemedBy,
    bool CanRevoke);

public sealed record MatchPassesDto(
    Guid SquadId,
    Guid MatchId,
    [property: Description("Precio del acta interna y del vídeo de este partido (el suyo o, si no tiene, el general del club).")]
    int SheetPriceCents,
    int VideoPriceCents,
    string Currency,
    [property: Description("Lo que se queda el club de cada venta (Passes:ClubSharePercent); el resto es de la plataforma.")]
    int ClubSharePercent,
    bool HasSheet,
    bool HasVideo,
    [property: Description("La plantilla es pública: otros usuarios pueden solicitar el acceso.")]
    bool OpenToRequests,
    IReadOnlyList<MatchPassDto> Passes);

public sealed record PassPreviewDto(
    string Code,
    MatchPassStatus Status,
    Guid SquadId,
    Guid TeamId,
    string TeamName,
    string ClubName,
    MatchDto Match,
    bool IncludesSheet,
    bool IncludesVideo,
    bool AllowsDownload,
    int PriceCents,
    string Currency,
    DateTimeOffset ExpiresAt,
    [property: Description("Lo canjeó la cuenta que pregunta.")]
    bool Mine,
    [property: Description("Es para otra persona (salió de una solicitud ajena) o ya lo canjeó otra.")]
    bool ForSomeoneElse);

public sealed record MyPassDto(
    Guid Id,
    string Code,
    Guid SquadId,
    Guid TeamId,
    string TeamName,
    MatchDto Match,
    bool IncludesSheet,
    bool IncludesVideo,
    bool AllowsDownload,
    int PriceCents,
    string Currency,
    DateTimeOffset RedeemedAt);

public sealed record MyPassRequestDto(
    Guid Id,
    Guid SquadId,
    Guid TeamId,
    string TeamName,
    MatchDto Match,
    bool WantsSheet,
    bool WantsVideo,
    string? Message,
    MatchPassRequestStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    [property: Description("Código del pase aprobado, mientras no se haya canjeado.")]
    string? PassCode,
    int? PassPriceCents,
    bool PassIncludesSheet,
    bool PassIncludesVideo,
    bool PassRedeemed);

public sealed record MyPassesDto(IReadOnlyList<MyPassDto> Passes, IReadOnlyList<MyPassRequestDto> Requests);

public sealed record ClubPassRequestDto(
    Guid Id,
    Guid SquadId,
    Guid TeamId,
    string TeamName,
    MatchDto Match,
    string RequesterName,
    bool WantsSheet,
    bool WantsVideo,
    string? Message,
    MatchPassRequestStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    bool HasVideo,
    int SheetPriceCents,
    int VideoPriceCents,
    string Currency,
    string? PassCode,
    bool PassRedeemed);

internal static class PassAccess
{
    public static async Task<IReadOnlyDictionary<(Guid SquadId, Guid MatchId), MatchInsight>> AllAsync(
        ICurrentUser user, IMatchPassStore passes, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId) return new Dictionary<(Guid, Guid), MatchInsight>();

        return (await passes.GetRedeemedByAsync(userId, cancellationToken))
            .Where(p => p.Grants(userId))
            .GroupBy(p => (p.SquadId, p.MatchId))
            .ToDictionary(
                g => g.Key,
                g => new MatchInsight(g.Any(p => p.IncludesSheet), g.Any(p => p.IncludesVideo), g.Any(p => p.AllowsDownload)));
    }

    public static async Task<MatchInsight> ForAsync(ICurrentUser user, Guid squadId, Guid matchId, IMatchPassStore passes, CancellationToken cancellationToken) =>
        (await AllAsync(user, passes, cancellationToken)).GetValueOrDefault((squadId, matchId), MatchInsight.None);
}

internal static class PassContext
{
    // El club que gestiona el equipo de la plantilla: es quien vende y cobra.
    public static async Task<Club> OwnerClubAsync(Squad squad, IClubStore clubs, CancellationToken cancellationToken) =>
        await clubs.GetByTeamAsync(squad.TeamId, cancellationToken)
            ?? throw new ConflictException(ErrorCode.PassNoClub);

    public static async Task<Match> PlayedMatchOfSquadAsync(Squad squad, Guid matchId, IMatchStore matches, TimeProvider clock, CancellationToken cancellationToken)
    {
        var match = await matches.GetByIdAsync(matchId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.MatchNotFound, matchId);

        if (match.SeasonId != squad.SeasonId || !match.Involves(squad.TeamId))
            throw new ConflictException(ErrorCode.MatchNotOfSquad);

        if (!match.IsOver(clock.GetUtcNow()))
            throw new ConflictException(ErrorCode.PassMatchNotPlayed);

        return match;
    }

    public static async Task<(int Sheet, int Video)> PricesAsync(Club club, Guid squadId, Guid matchId, IMatchPassPriceStore prices, CancellationToken cancellationToken)
    {
        var custom = await prices.GetAsync(squadId, matchId, cancellationToken);
        return (custom?.SheetPriceCents ?? club.SheetPassPriceCents, custom?.VideoPriceCents ?? club.VideoPassPriceCents);
    }

    public static int Total((int Sheet, int Video) prices, bool sheet, bool video) =>
        (sheet ? prices.Sheet : 0) + (video ? prices.Video : 0);

    // Nombre con el que el club ve a quien solicita o canjea; nunca el correo.
    public static async Task<string> AccountNameAsync(ICurrentUser user, IClubStore clubs, ICoachStore coaches, IPlayerStore players, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId) throw new UnauthenticatedException();

        if (user.ClubId is not null && await clubs.GetByUserIdAsync(userId, cancellationToken) is { } club)
            return club.Name;

        if (user.CoachId is not null && await coaches.GetByUserIdAsync(userId, cancellationToken) is { } coach)
            return $"{coach.DisplayName} (técnico)";

        if (user.PlayerId is not null && await players.GetByUserIdAsync(userId, cancellationToken) is { } player)
            return $"{player.DisplayName} (jugador)";

        return "Cuenta de Tercer Tiempo";
    }

    public static async Task<IReadOnlyDictionary<Guid, Team>> TeamsOfAsync(IEnumerable<Match> matches, ITeamStore teams, CancellationToken cancellationToken) =>
        (await teams.GetByIdsAsync(matches.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct(), cancellationToken)).ToDictionary(t => t.Id);

    public static MatchPassDto ToDto(MatchPass pass, DateTimeOffset now) =>
        new(pass.Id, pass.Code, pass.IncludesSheet, pass.IncludesVideo, pass.AllowsDownload, pass.PriceCents, pass.Currency,
            pass.Recipient, pass.StatusOn(now), pass.RequestId is not null, pass.CreatedAt, pass.ExpiresAt, pass.RedeemedAt,
            pass.RedeemedByName, !pass.IsRevoked && (!pass.IsRedeemed || pass.IsFree));

    public static MyPassDto ToMine(MatchPass pass, IReadOnlyDictionary<Guid, Match> matchMap, IReadOnlyDictionary<Guid, Team> teamMap) =>
        new(pass.Id, pass.Code, pass.SquadId, pass.TeamId, teamMap.GetValueOrDefault(pass.TeamId)?.Name ?? "Equipo",
            MatchMapper.ToDto(matchMap[pass.MatchId], teamMap), pass.IncludesSheet, pass.IncludesVideo, pass.AllowsDownload,
            pass.PriceCents, pass.Currency, pass.RedeemedAt ?? pass.CreatedAt);
}
