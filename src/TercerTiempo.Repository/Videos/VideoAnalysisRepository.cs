using System.ComponentModel;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Videos.Analysis;

namespace TercerTiempo.Repository.Videos;

public sealed record RequestVideoAnalysisRequest(
    [property: Description("Vídeo que se quiere analizar; sin él, todos los de la plantilla en este partido que se puedan analizar.")]
    Guid? VideoId = null);

public sealed record CancelVideoAnalysisRequest(
    [property: Description("Vídeo cuyo análisis se cancela, se pausa o se reanuda; sin él, todos los de la plantilla en este partido a los que les toque.")]
    Guid? VideoId = null);

public sealed record ApplyVideoAnalysisRequest(
    [property: Description("Un elemento por vídeo que se vuelca: cuál de los dos equipos detectados es el del club y a qué ficha corresponde cada jugador seguido. Con las dos partes del partido, lo de las dos se suma.")]
    IReadOnlyList<ApplyVideoPart> Videos,
    [property: Description("Datos de equipo que se aceptan: Possession, Shots, ShotsOnTarget, Corners, ThrowIns, GoalKicks, Fouls (cometidas y recibidas) y Offsides (los nuestros y los del rival). Cada uno lleva el valor del equipo y el del rival.")]
    IReadOnlyList<VideoTeamField>? TeamFields = null,
    [property: Description("true para confirmar que se sobrescriben los datos del acta que el vídeo cambiaría. Sin él, la respuesta es 409 con la lista.")]
    bool Overwrite = false,
    [property: Description("Con el acta oficial cerrada, los minutos (por descarte: sin cambio, el partido entero), la titularidad, los goles y las tarjetas de cada jugador salen de ella, también para los que jugaron y no se identificaron en el vídeo. Por defecto, sí.")]
    bool FillFromOfficialSheet = true)
{
    internal ApplyVideoAnalysisCommand ToCommand(Guid squadId, Guid matchId) => new(squadId, matchId, Videos, TeamFields ?? [], Overwrite, FillFromOfficialSheet);
}

// La propuesta de acta interna que sale del vídeo del partido: verla, volver a pedirla y volcarla al acta.
public interface IVideoAnalysisRepository
{
    Task<MatchVideoAnalysisDto> GetAsync(Guid squadId, Guid matchId, CancellationToken cancellationToken = default);
    Task<VideoAnalysisStampDto> GetStampAsync(Guid squadId, Guid matchId, CancellationToken cancellationToken = default);
    Task<MatchVideoAnalysisDto> RequestAsync(Guid squadId, Guid matchId, RequestVideoAnalysisRequest? request, CancellationToken cancellationToken = default);
    Task<MatchVideoAnalysisDto> CancelAsync(Guid squadId, Guid matchId, CancelVideoAnalysisRequest? request, CancellationToken cancellationToken = default);
    Task<MatchVideoAnalysisDto> PauseAsync(Guid squadId, Guid matchId, CancelVideoAnalysisRequest? request, bool paused, CancellationToken cancellationToken = default);
    Task<ApplyVideoAnalysisResultDto> ApplyAsync(Guid squadId, Guid matchId, ApplyVideoAnalysisRequest request, CancellationToken cancellationToken = default);
    Task<VideoAnalysisCropDto> GetCropAsync(Guid squadId, Guid matchId, int tracklet, Guid? videoId, CancellationToken cancellationToken = default);
}

internal sealed class VideoAnalysisRepository(
    IQueryHandler<GetVideoAnalysisQuery, MatchVideoAnalysisDto> get,
    IQueryHandler<GetVideoAnalysisStampQuery, VideoAnalysisStampDto> stamp,
    ICommandHandler<RequestVideoAnalysisCommand, MatchVideoAnalysisDto> request,
    ICommandHandler<CancelVideoAnalysisCommand, MatchVideoAnalysisDto> cancel,
    ICommandHandler<PauseVideoAnalysisCommand, MatchVideoAnalysisDto> pause,
    ICommandHandler<ApplyVideoAnalysisCommand, ApplyVideoAnalysisResultDto> apply,
    IQueryHandler<GetVideoAnalysisCropQuery, VideoAnalysisCropDto> crop) : IVideoAnalysisRepository
{
    public Task<MatchVideoAnalysisDto> GetAsync(Guid squadId, Guid matchId, CancellationToken cancellationToken = default) =>
        get.Handle(new GetVideoAnalysisQuery(squadId, matchId), cancellationToken);

    public Task<VideoAnalysisStampDto> GetStampAsync(Guid squadId, Guid matchId, CancellationToken cancellationToken = default) =>
        stamp.Handle(new GetVideoAnalysisStampQuery(squadId, matchId), cancellationToken);

    public Task<MatchVideoAnalysisDto> RequestAsync(Guid squadId, Guid matchId, RequestVideoAnalysisRequest? body, CancellationToken cancellationToken = default) =>
        request.Handle(new RequestVideoAnalysisCommand(squadId, matchId, body?.VideoId), cancellationToken);

    public Task<MatchVideoAnalysisDto> CancelAsync(Guid squadId, Guid matchId, CancelVideoAnalysisRequest? body, CancellationToken cancellationToken = default) =>
        cancel.Handle(new CancelVideoAnalysisCommand(squadId, matchId, body?.VideoId), cancellationToken);

    public Task<MatchVideoAnalysisDto> PauseAsync(Guid squadId, Guid matchId, CancelVideoAnalysisRequest? body, bool paused, CancellationToken cancellationToken = default) =>
        pause.Handle(new PauseVideoAnalysisCommand(squadId, matchId, body?.VideoId, paused), cancellationToken);

    public Task<ApplyVideoAnalysisResultDto> ApplyAsync(Guid squadId, Guid matchId, ApplyVideoAnalysisRequest body, CancellationToken cancellationToken = default) =>
        apply.Handle(body.ToCommand(squadId, matchId), cancellationToken);

    public Task<VideoAnalysisCropDto> GetCropAsync(Guid squadId, Guid matchId, int tracklet, Guid? videoId, CancellationToken cancellationToken = default) =>
        crop.Handle(new GetVideoAnalysisCropQuery(squadId, matchId, tracklet, videoId), cancellationToken);
}
