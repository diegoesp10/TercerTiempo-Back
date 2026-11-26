using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Application.Videos.Analysis;

public sealed record VideoAnalysisStampDto(
    [property: Description("La misma huella que trae el análisis (stamp) mientras no cambie nada de lo que se ve.")]
    string Stamp);

// Lo que pregunta cada pocos segundos el acta interna mientras se analiza: si la huella no coincide con la del análisis que
// tiene, lo vuelve a pedir entero.
public sealed record GetVideoAnalysisStampQuery(Guid SquadId, Guid MatchId) : IQuery<VideoAnalysisStampDto>;

internal static class VideoAnalysisStamp
{
    // Todo lo que enseña el análisis mientras avanza: el estado y el avance (también de la compresión del vídeo), lo que
    // cuenta en directo, el registro, las pausas, lo volcado y si hay algún analizador encendido.
    public static string Of(IEnumerable<VideoAnalysisStampRow> rows, bool analyzerOnline)
    {
        var text = new StringBuilder(analyzerOnline ? "con-analizador" : "sin-analizador");
        foreach (var row in rows.OrderBy(r => r.VideoId))
        {
            text.Append('|').Append(row.VideoId).Append(';').Append((int)row.VideoStatus).Append(';').Append(row.VideoProgress)
                .Append(';').Append(row.AnalysisId).Append(';').Append((int?)row.Status).Append(';').Append(row.Progress)
                .Append(';').Append(row.Attempts).Append(';').Append(row.RequestedAt?.UtcTicks).Append(';').Append(row.StartedAt?.UtcTicks)
                .Append(';').Append(row.FinishedAt?.UtcTicks).Append(';').Append(row.LiveAt?.UtcTicks).Append(';').Append(row.PausedAt?.UtcTicks)
                .Append(';').Append(row.PausedSeconds).Append(';').Append(row.Working ? 1 : 0).Append(';').Append(row.AppliedAt?.UtcTicks)
                .Append(';').Append(row.OurTeam).Append(';').Append(row.LogLength);
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }

    public static VideoAnalysisStampRow Row(MatchVideo video, MatchVideoAnalysis? analysis) => new(
        video.Id, video.Status, video.ProcessingProgress,
        analysis?.Id, analysis?.Status, analysis?.Progress ?? 0, analysis?.Attempts ?? 0, analysis?.RequestedAt, analysis?.StartedAt,
        analysis?.FinishedAt, analysis?.LiveAt, analysis?.PausedAt, analysis?.PausedSeconds ?? 0, analysis?.IsWorking ?? false,
        analysis?.AppliedAt, analysis?.OurTeam, analysis?.LogJson?.Length ?? 0);
}

// Solo la huella: ni la propuesta, ni el acta oficial, ni los recortes. Mismos permisos que el análisis.
internal sealed class GetVideoAnalysisStampHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IMatchVideoAnalysisStore analyses,
    IVideoAnalyzerHeartbeatStore analyzers,
    TimeProvider clock) : IQueryHandler<GetVideoAnalysisStampQuery, VideoAnalysisStampDto>
{
    public async Task<VideoAnalysisStampDto> Handle(GetVideoAnalysisStampQuery query, CancellationToken cancellationToken)
    {
        var access = await SquadAccess.ManagerAccessAsync(squads, currentUser, query.SquadId, clock, cancellationToken);
        var rows = await analyses.GetStampRowsAsync(access.Id, query.MatchId, cancellationToken);
        var seen = await analyzers.LastSeenAsync(cancellationToken);
        return new VideoAnalysisStampDto(VideoAnalysisStamp.Of(rows, VideoAnalyzerHeartbeat.IsOnline(seen, clock.GetUtcNow())));
    }
}
