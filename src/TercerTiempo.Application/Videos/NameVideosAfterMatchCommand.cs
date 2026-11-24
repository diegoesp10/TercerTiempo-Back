using Microsoft.Extensions.Logging;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Application.Videos;

public sealed record NameVideosAfterMatchCommand : ICommand<int>;

internal sealed class NameVideosAfterMatchHandler(
    IMatchVideoStore videos,
    IMatchStore matches,
    ITeamStore teams,
    IUnitOfWork unitOfWork,
    ILogger<NameVideosAfterMatchHandler> logger) : ICommandHandler<NameVideosAfterMatchCommand, int>
{
    public async Task<int> Handle(NameVideosAfterMatchCommand command, CancellationToken cancellationToken)
    {
        var all = await videos.GetAllAsync(cancellationToken);
        if (all.Count == 0) return 0;

        var matchMap = (await matches.GetByIdsAsync(all.Select(v => v.MatchId).Distinct(), cancellationToken)).ToDictionary(m => m.Id);
        var names = (await teams.GetByIdsAsync(matchMap.Values.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct(), cancellationToken))
            .ToDictionary(t => t.Id, t => t.Name);

        var renamed = 0;
        foreach (var video in all)
        {
            if (!matchMap.TryGetValue(video.MatchId, out var match)) continue;

            var title = MatchVideoNames.Title(
                names.GetValueOrDefault(match.HomeTeamId, "Local"), names.GetValueOrDefault(match.AwayTeamId, "Visitante"), match.KickoffAt, video.Part);
            if (video.Rename(title, MatchVideoNames.FileName(title, video.FileName, video.ContentType))) renamed++;
        }

        if (renamed == 0) return 0;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Vídeos renombrados con el nombre del partido: {Count}", renamed);
        return renamed;
    }
}
