using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.OfficialSheets;

// Completa el acta pública desde el proceso en segundo plano (Background = true). Se consultan las webs en el orden de
// OfficialSheets:Sources y vale la primera que la tenga completa; FederationAllowed = false se salta la federación.
// Force se conserva por compatibilidad; nunca evita la lectura previa de SQL ni los límites de descarga.
// StopWhenThrottled: si una web está en pausa no se sigue con la siguiente, se lanza la pausa (la cola pedida desde la
// administración espera a que acabe en lugar de dar el acta por no publicada).
public sealed record SyncOfficialSheetCommand(
    Guid MatchId, bool Force = false, bool Background = false, bool FederationAllowed = true, bool StopWhenThrottled = false) : ICommand<OfficialSheetDto>;

internal sealed class SyncOfficialSheetValidator : AbstractValidator<SyncOfficialSheetCommand>
{
    public SyncOfficialSheetValidator() => RuleFor(x => x.MatchId).NotEmpty();
}

internal sealed class SyncOfficialSheetHandler(
    IEnumerable<IPublicSheetSource> sources,
    IOptions<PublicSheetOptions> options,
    IMatchStore matches,
    ICompetitionStore competitions,
    IOfficialSheetStore sheets,
    IUnitOfWork unitOfWork,
    OfficialSheetSyncMemory memory,
    TimeProvider clock,
    ILogger<SyncOfficialSheetHandler> logger) : ICommandHandler<SyncOfficialSheetCommand, OfficialSheetDto>
{
    public async Task<OfficialSheetDto> Handle(SyncOfficialSheetCommand command, CancellationToken cancellationToken)
    {
        var match = await matches.GetByIdAsync(command.MatchId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.MatchNotFound, command.MatchId);

        var now = clock.GetUtcNow();
        var sheet = await sheets.GetStoredByMatchAsync(match.Id, cancellationToken);
        // Un acta de una web desactivada no se muestra ni cuenta como completa: se busca en las webs activas.
        var hidden = sheet is not null && !options.Value.Allows(sheet.Provider);

        // Ni Force ni una petición manual deben volver a descargar un acta completa ya almacenada.
        if (sheet is { IsAvailable: true } && !hidden)
            return Result(sheet, match, OfficialSheetAvailability.Available, null);

        var visible = hidden ? null : sheet;
        if (match.Status is MatchStatus.Cancelled)
            return Result(visible, match, OfficialSheetAvailability.Unavailable, "El partido está cancelado.");

        if (match.Status is MatchStatus.Scheduled or MatchStatus.Postponed && match.KickoffAt > now)
            return Result(visible, match, OfficialSheetAvailability.NotPlayed, "El partido todavía no se ha jugado.");

        if (sheet is not null && !OfficialSheetRefresh.IsDue(sheet, match, now, hidden))
            return Result(visible, match, OfficialSheetAvailability.Pending, null);

        var (competition, season) = await Resolvers.SeasonByIdAsync(competitions, match.SeasonId, cancellationToken);

        SheetFetch? pending = null;
        var unreachable = false;
        foreach (var source in Ordered())
        {
            if (source.IsFederation && !command.FederationAllowed) continue;

            SheetFetch? fetch;
            try
            {
                fetch = await source.FetchAsync(match, competition, season, sheet, cancellationToken);
            }
            catch (ProviderThrottledException ex) when (!command.StopWhenThrottled)
            {
                // Esa web está en pausa: se prueba la siguiente; esta volverá a intentarse en la próxima comprobación.
                logger.LogInformation("{Provider}: en pausa ({Message}); se prueba la siguiente web para el partido {MatchId}", source.Name, ex.Message, match.Id);
                unreachable = true;
                continue;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested
                && !(ex is ProviderThrottledException && command.StopWhenThrottled))
            {
                logger.LogWarning(ex, "{Provider}: no se pudo consultar el acta del partido {MatchId}", source.Name, match.Id);
                unreachable = true;
                continue;
            }

            if (fetch is null) continue;
            if (!fetch.Complete)
            {
                pending ??= fetch;
                continue;
            }

            sheet = Link(sheet, match, fetch, now);
            sheet.Apply(fetch.Data!, now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result(sheet, match, OfficialSheetAvailability.Available, null);
        }

        if (sheet is null && pending is not null)
            sheet = Link(null, match, pending, now);
        sheet?.MarkChecked(now);
        // Guarda también lo que se haya localizado por el camino (códigos de equipo y partido de la federación).
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (sheet is not null && !hidden)
            return Result(sheet, match, OfficialSheetAvailability.Pending,
                unreachable ? "No se ha podido consultar alguna web de actas; se volverá a intentar." : "Todavía no se han publicado las alineaciones.");

        // Solo se da por ausente si se ha preguntado a todas las webs sin errores ni pausas.
        if (!unreachable && command.FederationAllowed)
            memory.MatchNotFound(match.Id, now);

        return OfficialSheetMapper.Empty(match.Id,
            unreachable ? OfficialSheetAvailability.Pending : OfficialSheetAvailability.Unavailable, null,
            unreachable ? "No se ha podido consultar alguna web de actas; se volverá a intentar." : "No se ha encontrado este partido en ninguna web de actas.");
    }

    private IEnumerable<IPublicSheetSource> Ordered()
    {
        var order = options.Value.EnabledSources.Select((name, index) => (name, index))
            .ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);
        return sources.Where(s => order.ContainsKey(s.Name)).OrderBy(s => order[s.Name]);
    }

    private OfficialMatchSheet Link(OfficialMatchSheet? sheet, Match match, SheetFetch fetch, DateTimeOffset now)
    {
        if (sheet is null)
        {
            sheet = OfficialMatchSheet.Create(match.Id, fetch.Provider, fetch.ExternalId, fetch.SourceUrl, now);
            sheets.Add(sheet);
        }
        else if (sheet.Provider != fetch.Provider || sheet.ExternalId != fetch.ExternalId)
        {
            sheet.LinkSource(fetch.Provider, fetch.ExternalId, fetch.SourceUrl);
        }
        return sheet;
    }

    private static OfficialSheetDto Result(OfficialMatchSheet? sheet, Match match, OfficialSheetAvailability status, string? message) =>
        sheet is null
            ? OfficialSheetMapper.Empty(match.Id, status, null, message)
            : OfficialSheetMapper.ToDto(sheet, match.HomeTeamId, match.AwayTeamId, sheet.IsAvailable ? OfficialSheetAvailability.Available : status, message);
}
