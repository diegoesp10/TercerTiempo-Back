using System.Security.Cryptography;
using System.Text;
using TercerTiempo.Domain.Trainings;

namespace TercerTiempo.Application.Trainings;

// Huella de lo que se ve de una convocatoria: quién va, qué ha firmado cada uno, a quién le ha llegado y quién la ha
// abierto, la cita y si el partido ya ha empezado. La página de la convocatoria la pregunta cada pocos segundos y solo
// vuelve a pedir la convocatoria entera cuando cambia (la entera calcula lesiones, sanciones y campos). Un borrador da la
// misma huella que no tener convocatoria: los jugadores no lo ven y solo lo cambia quien lo prepara.
internal static class CallUpStamp
{
    public static string Of(MatchCallUp? callUp, bool closed)
    {
        var text = new StringBuilder(closed ? "cerrada" : "abierta");
        if (callUp is { IsPublished: true })
        {
            text.Append('|').Append(callUp.Id).Append('|').Append(callUp.UpdatedAt.UtcTicks);
            foreach (var entry in callUp.Entries.OrderBy(e => e.SquadPlayerId))
                text.Append('|').Append(entry.SquadPlayerId).Append(entry.Called ? '+' : '-').Append((int)entry.Response)
                    .Append(';').Append(entry.NotifiedAt?.UtcTicks).Append(';').Append(entry.SeenAt?.UtcTicks);
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }
}
