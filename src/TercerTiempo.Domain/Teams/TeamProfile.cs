using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Teams;

public sealed class TeamProfile
{
    // Los mismos márgenes que acepta el analizador de vídeo (--campo LxA).
    public const decimal MinPitchLengthM = 60, MaxPitchLengthM = 130, MinPitchWidthM = 35, MaxPitchWidthM = 90;

    public string? OfficialName { get; init; }
    public string? Locality { get; init; }
    public string? Province { get; init; }
    public string? Address { get; init; }
    public string? PostalCode { get; init; }
    public string? Website { get; init; }

    public string? Ground { get; init; }

    public string? GroundSurface { get; init; }
    public string? GroundAddress { get; init; }
    public string? GroundLocality { get; init; }

    public string? ShirtColor { get; init; }
    public string? ShortsColor { get; init; }
    public string? SocksColor { get; init; }

    // Medidas del terreno de juego en metros. No vienen de la federación: las apunta el club del equipo, y con ellas
    // el analizador de vídeo calibra el campo de los partidos que se juegan en él.
    public decimal? PitchLengthM { get; init; }
    public decimal? PitchWidthM { get; init; }

    public bool HasPitch => PitchLengthM is not null && PitchWidthM is not null;

    public TeamProfile WithPitch(decimal? lengthM, decimal? widthM)
    {
        if ((lengthM is null) != (widthM is null))
            throw new DomainException(ErrorCode.TeamPitchIncomplete);
        if (lengthM is { } length) Guard.InRange(length, MinPitchLengthM, MaxPitchLengthM, "largo del campo");
        if (widthM is { } width) Guard.InRange(width, MinPitchWidthM, MaxPitchWidthM, "ancho del campo");

        return new TeamProfile
        {
            OfficialName = OfficialName,
            Locality = Locality,
            Province = Province,
            Address = Address,
            PostalCode = PostalCode,
            Website = Website,
            Ground = Ground,
            GroundSurface = GroundSurface,
            GroundAddress = GroundAddress,
            GroundLocality = GroundLocality,
            ShirtColor = ShirtColor,
            ShortsColor = ShortsColor,
            SocksColor = SocksColor,
            PitchLengthM = lengthM is { } l ? Math.Round(l, 1) : null,
            PitchWidthM = widthM is { } w ? Math.Round(w, 1) : null,
        };
    }

    public bool IsEmpty =>
        OfficialName is null && Locality is null && Province is null && Address is null && PostalCode is null && Website is null
        && Ground is null && GroundSurface is null && GroundAddress is null && GroundLocality is null
        && ShirtColor is null && ShortsColor is null && SocksColor is null
        && PitchLengthM is null && PitchWidthM is null;
}
