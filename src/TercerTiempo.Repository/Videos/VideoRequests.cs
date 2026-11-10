using System.ComponentModel;
using TercerTiempo.Application.Videos;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Repository.Videos;

public sealed record BeginVideoUploadRequest(
    [property: Description("Full (partido completo), FirstHalf o SecondHalf.")]
    VideoPart Part,
    [property: Description("Título opcional; si falta se usa el nombre del archivo.")]
    string? Title,
    string FileName,
    [property: Description("video/mp4 o video/webm.")]
    string ContentType,
    long SizeBytes)
{
    internal BeginVideoUploadCommand ToCommand(Guid squadId, Guid matchId) => new(squadId, matchId, Part, Title, FileName, ContentType, SizeBytes);
}
