namespace TercerTiempo.Domain.Teams;

/// <summary>
/// Ficha del club según la federación: nombre oficial, sede, web, equipación y el campo donde juega de local.
/// Se guarda como tipo propio del equipo; todos los campos son opcionales porque la federación no siempre los rellena.
/// </summary>
public sealed class TeamProfile
{
    public string? OfficialName { get; init; }
    public string? Locality { get; init; }
    public string? Province { get; init; }
    public string? Address { get; init; }
    public string? PostalCode { get; init; }
    public string? Website { get; init; }

    /// <summary>Nombre del campo donde juega de local.</summary>
    public string? Ground { get; init; }

    /// <summary>Superficie del campo (hierba natural, hierba sintética, tierra...).</summary>
    public string? GroundSurface { get; init; }
    public string? GroundAddress { get; init; }
    public string? GroundLocality { get; init; }

    public string? ShirtColor { get; init; }
    public string? ShortsColor { get; init; }
    public string? SocksColor { get; init; }

    public bool IsEmpty =>
        OfficialName is null && Locality is null && Province is null && Address is null && PostalCode is null && Website is null
        && Ground is null && GroundSurface is null && GroundAddress is null && GroundLocality is null
        && ShirtColor is null && ShortsColor is null && SocksColor is null;
}
