using System.ComponentModel;
using TercerTiempo.Application.Performances.LogPerformance;

namespace TercerTiempo.Repository.Matches;

/// <summary>Datos de rendimiento que envía el cliente; el partido llega por la ruta.</summary>
public sealed record LogPerformanceRequest(
    [property: Description("true si saliste de inicio.")]
    bool Started,
    [property: Description("Minutos jugados, 0-120. Con 0 minutos no se admiten acciones de juego.")]
    int MinutesPlayed,
    [property: Description("Goles marcados.")]
    int Goals,
    [property: Description("Asistencias de gol.")]
    int Assists,
    [property: Description("Tiros totales.")]
    int Shots,
    [property: Description("Tiros a puerta (no puede superar los tiros totales).")]
    int ShotsOnTarget,
    [property: Description("Pases clave (que acaban en ocasión).")]
    int KeyPasses,
    [property: Description("Entradas.")]
    int Tackles,
    [property: Description("Intercepciones.")]
    int Interceptions,
    [property: Description("Paradas (porteros).")]
    int Saves,
    [property: Description("Tarjetas amarillas, 0-2. Dos amarillas implican redCard = true.")]
    int YellowCards,
    [property: Description("true si fuiste expulsado.")]
    bool RedCard,
    [property: Description("Tu valoración del partido, de 1 a 10.")]
    int SelfRating,
    [property: Description("Notas libres (máximo 1000 caracteres).")]
    string? Notes)
{
    internal LogPerformanceCommand ToCommand(Guid matchId) => new(
        matchId, Started, MinutesPlayed, Goals, Assists, Shots, ShotsOnTarget, KeyPasses,
        Tackles, Interceptions, Saves, YellowCards, RedCard, SelfRating, Notes);
}
