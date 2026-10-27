using TercerTiempo.Application.Abstractions.FootballData;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Referees;

namespace TercerTiempo.Application.Referees;

// El trabajo de los equipos arbitrales: primero lo que dicen las actas de la federación ya guardadas (se releen de la
// caché, sin pedir nada) y después, para los partidos jugados que sigan sin él, la página del partido en
// futbolasturiano.es, que solo lo publica en algunos (se vuelve a mirar unos días después por si lo añade).
internal sealed class SyncMatchOfficialsHandler(
    IRefereeStore referees,
    IOfficialSheetProvider sheets,
    IFootballDataProvider footballData,
    MatchOfficialsWriter writer,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<SyncMatchOfficialsCommand, MatchOfficialsSynced>
{
    private const int LookupBatch = 60;
    private const int MaxAttempts = 3;
    private static readonly TimeSpan RetryAfter = TimeSpan.FromDays(3);

    public async Task<MatchOfficialsSynced> Handle(SyncMatchOfficialsCommand command, CancellationToken cancellationToken)
    {
        var fromSheets = 0;
        foreach (var source in await referees.GetSheetsWithoutOfficialsAsync(sheets.ProviderName, cancellationToken))
        {
            if (await sheets.GetCachedSheetAsync(source.ExternalId, cancellationToken) is not { Officials: { Count: > 0 } officials }) continue;
            if (await writer.ApplyAsync(source.MatchId, officials, sheets.ProviderName, authoritative: true, cancellationToken) > 0) fromSheets++;
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var fromProvider = 0;
        var looked = 0;
        var now = clock.GetUtcNow();
        foreach (var source in await referees.GetMatchesToLookUpAsync(footballData.ProviderName, now, RetryAfter, MaxAttempts, LookupBatch, cancellationToken))
        {
            // null: la página no se ha podido leer; se intenta en la próxima pasada sin contarlo.
            var officials = await footballData.GetMatchOfficialsAsync(source.ExternalId, cancellationToken);
            if (officials is null) continue;
            looked++;

            if (officials.Count > 0 && await writer.ApplyAsync(source.MatchId, officials, footballData.ProviderName, authoritative: false, cancellationToken) > 0)
            {
                fromProvider++;
            }
            else if (await referees.GetLookupAsync(source.MatchId, cancellationToken) is { } lookup)
            {
                lookup.Again(now);
            }
            else
            {
                referees.AddLookup(MatchOfficialLookup.First(source.MatchId, now));
            }
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new MatchOfficialsSynced(fromSheets, fromProvider, looked);
    }
}
