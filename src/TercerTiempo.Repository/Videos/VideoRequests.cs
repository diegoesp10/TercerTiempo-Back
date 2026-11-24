using System.ComponentModel;
using TercerTiempo.Application.Videos;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Repository.Videos;

public sealed record BeginVideoUploadRequest(
    [property: Description("Full (partido completo), FirstHalf o SecondHalf.")]
    VideoPart Part,
    [property: Description("Nombre del archivo en el equipo de quien sube: sirve para reanudar y por su extensión; el vídeo se llama como el partido («Local vs Visitante dd-mm-aaaa»).")]
    string FileName,
    [property: Description("video/mp4 o video/webm.")]
    string ContentType,
    long SizeBytes)
{
    internal BeginVideoUploadCommand ToCommand(Guid squadId, Guid matchId) => new(squadId, matchId, Part, FileName, ContentType, SizeBytes);
}
