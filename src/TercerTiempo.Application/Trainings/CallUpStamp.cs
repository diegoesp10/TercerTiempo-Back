using System.Security.Cryptography;
using System.Text;
using TercerTiempo.Domain.Trainings;

namespace TercerTiempo.Application.Trainings;

// Huella de lo que se ve de una convocatoria: quién va, qué ha firmado cada uno, a quién le ha llegado y quién la ha
// abierto, la cita y si el partido ya ha empezado. La página de la convocatoria la pregunta cada pocos segundos y solo
// vuelve a pedir la convocatoria entera cuando cambia (la entera calcula lesiones, sanciones y campos). Un borrador da la
// misma huella que no tener convocatoria: los jugadores no lo ven y solo lo cambia quien lo prepara.
// La ruta ligera la calcula con la proyección (CallUpStampRow) y la convocatoria entera con la entidad: las dos pasan por
// Row, así que dan la misma.
internal static class CallUpStamp
{
    public static string Of(MatchCallUp? callUp, bool closed) => Of(Row(callUp), closed);

    public static string Of(CallUpStampRow? row, bool closed)
    {
        var text = new StringBuilder(closed ? "cerrada" : "abierta");
        if (row is not null)
        {
            text.Append('|').Append(row.Id).Append('|').Append(row.UpdatedAt.UtcTicks);
            foreach (var line in row.Lines.OrderBy(e => e.SquadPlayerId))
                text.Append('|').Append(line.SquadPlayerId).Append(line.Called ? '+' : '-').Append((int)line.Response)
                    .Append(';').Append(line.NotifiedAt?.UtcTicks).Append(';').Append(line.SeenAt?.UtcTicks);
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }

    public static CallUpStampRow? Row(MatchCallUp? callUp) =>
        callUp is { IsPublished: true }
            ? new CallUpStampRow(callUp.Id, callUp.UpdatedAt,
                callUp.Entries.Select(e => new CallUpStampLine(e.SquadPlayerId, e.Called, e.Response, e.NotifiedAt, e.SeenAt)).ToList())
            : null;
}
