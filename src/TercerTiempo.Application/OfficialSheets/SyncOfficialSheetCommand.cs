using FluentValidation;
using Microsoft.Extensions.Logging;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.OfficialSheets;

// Completa datos que falten desde el proceso en segundo plano (Background = true).
// Force se conserva por compatibilidad; nunca evita la lectura previa de SQL ni los límites de descarga.
public sealed record SyncOfficialSheetCommand(Guid MatchId, bool Force = false, bool Background = false) : ICommand<OfficialSheetDto>;

internal sealed class SyncOfficialSheetValidator : AbstractValidator<SyncOfficialSheetCommand>
{
    public SyncOfficialSheetValidator() => RuleFor(x => x.MatchId).NotEmpty();
}

internal sealed class SyncOfficialSheetHandler(
    IOfficialSheetProvider provider,
    OfficialSheetLocator locator,
    IMatchStore matches,
    ICompetitionStore competitions,
    IOfficialSheetStore sheets,
    IUnitOfWork unitOfWork,
    OfficialSheetSyncMemory memory,
    TimeProvider clock,
    ILogger<SyncOfficialSheetHandler> logger) : ICommandHandler<SyncOfficialSheetCommand, OfficialSheetDto>
{
    private static readonly TimeSpan NotBefore = TimeSpan.Zero;

    public async Task<OfficialSheetDto> Handle(SyncOfficialSheetCommand command, CancellationToken cancellationToken)
    {
        var match = await matches.GetByIdAsync(command.MatchId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.MatchNotFound, command.MatchId);

        var now = clock.GetUtcNow();
        var sheet = await sheets.GetByMatchAsync(match.Id, cancellationToken);

        // Ni Force ni una petición manual deben volver a descargar un acta completa ya almacenada.
        if (sheet is { IsAvailable: true })
            return Result(sheet, match, OfficialSheetAvailability.Available, null);

        if (match.Status is MatchStatus.Cancelled)
            return Result(sheet, match, OfficialSheetAvailability.Unavailable, "El partido está cancelado.");

        if (match.Status is MatchStatus.Scheduled or MatchStatus.Postponed && match.KickoffAt - NotBefore > now)
            return Result(sheet, match, OfficialSheetAvailability.NotPlayed, "El partido todavía no se ha jugado.");

        if (sheet is not null && !OfficialSheetRefresh.IsDue(sheet, match, now))
            return Result(sheet, match, sheet.IsAvailable ? OfficialSheetAvailability.Available : OfficialSheetAvailability.Pending, null);

        try
        {
            sheet = await FetchAsync(match, sheet, now, cancellationToken);
        }
        catch (ProviderThrottledException) when (command.Background)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            // Conserva las referencias ya resueltas antes de pausar el proceso hasta RetryAt.
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "{Provider}: no se pudo consultar el acta del partido {MatchId}", provider.ProviderName, match.Id);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return sheet is null
                ? OfficialSheetMapper.Empty(match.Id, OfficialSheetAvailability.Pending, provider.ProviderName, "No se ha podido contactar con la federación; se volverá a intentar.")
                : Result(sheet, match, sheet.IsAvailable ? OfficialSheetAvailability.Available : OfficialSheetAvailability.Pending, "No se ha podido actualizar el acta; se muestra la última copia.");
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (sheet is null)
        {
            memory.MatchNotFound(match.Id, now);
            return OfficialSheetMapper.Empty(match.Id, OfficialSheetAvailability.Unavailable, provider.ProviderName,
                "No se ha encontrado este partido en la federación.");
        }

        return Result(sheet, match,
            sheet.IsAvailable ? OfficialSheetAvailability.Available : OfficialSheetAvailability.Pending,
            sheet.IsAvailable ? null : "La federación todavía no ha publicado el acta.");
    }

    private async Task<OfficialMatchSheet?> FetchAsync(Match match, OfficialMatchSheet? sheet, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (sheet is null)
        {
            var (competition, season) = await Resolvers.SeasonByIdAsync(competitions, match.SeasonId, cancellationToken);
            if (memory.SkipSeason(season.Id, now)) return null;
            if (await locator.ResolveCompetitionAsync(competition, season, cancellationToken) is null)
            {
                memory.SeasonNotFound(season.Id, now);
                return null;
            }
            var officialRef = await locator.LocateAsync(match, competition, season, cancellationToken);
            if (officialRef is null)
                return null;

            sheet = OfficialMatchSheet.Create(match.Id, officialRef.Provider, officialRef.ExternalId, provider.SheetUrl(officialRef.ExternalId), now);
            sheets.Add(sheet);
        }

        var data = await provider.GetSheetAsync(sheet.ExternalId, cancellationToken);

        if (data is null || data.Lines.Count == 0)
            sheet.MarkChecked(now);
        else
            sheet.Apply(data, now);

        return sheet;
    }

    private OfficialSheetDto Result(OfficialMatchSheet? sheet, Match match, OfficialSheetAvailability status, string? message) =>
        sheet is null
            ? OfficialSheetMapper.Empty(match.Id, status, provider.ProviderName, message)
            : OfficialSheetMapper.ToDto(sheet, match.HomeTeamId, match.AwayTeamId, status, message);
}
