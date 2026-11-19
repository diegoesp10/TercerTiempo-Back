using System.ComponentModel;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Squads.Injuries;

public sealed record InjuryTypeDto(
    InjuryType Type,
    string Label,
    [property: Description("Duración media orientativa en días; null si no hay referencia (Otra).")]
    int? AverageDays);

public sealed record InjuryDto(
    Guid Id,
    Guid SquadPlayerId,
    string PlayerName,
    int? ShirtNumber,
    InjuryType Type,
    string TypeLabel,
    int? AverageDays,
    string? Reason,
    DateOnly StartedOn,
    DateOnly? ExpectedReturnOn,
    DateOnly? ReturnedOn,
    bool Active,
    [property: Description("Días de baja hasta hoy (o hasta el alta si ya la tiene).")]
    int DaysOut,
    [property: Description("Pruebas e informes de la lesión (radiografías, informes del hospital...), los últimos primero. Solo los abren el club, el cuerpo técnico y el propio jugador.")]
    IReadOnlyList<InjuryDocumentDto> Documents);

public sealed record InjuryDocumentDto(
    Guid Id,
    InjuryDocumentKind Kind,
    string KindLabel,
    string FileName,
    string ContentType,
    long SizeBytes,
    [property: Description("Nota de quien lo subió (qué prueba es, de qué día...); null si no puso ninguna.")]
    string? Note,
    DateTimeOffset UploadedAt,
    [property: Description("Dirección de la API que lo sirve: hay que pedirlo con la sesión iniciada (un enlace normal no manda el token).")]
    string Url);

internal static class InjuryMapper
{
    public static InjuryTypeDto ToDto(InjuryTypeInfo info) => new(info.Type, info.Label, info.AverageDays);

    public static InjuryDto ToDto(PlayerInjury injury, SquadPlayer? player, DateOnly today, IEnumerable<InjuryDocument>? documents = null)
    {
        var info = InjuryCatalog.Info(injury.Type);
        return new InjuryDto(
            injury.Id, injury.SquadPlayerId, player?.FullName ?? "Jugador", player?.ShirtNumber,
            injury.Type, info.Label, info.AverageDays, injury.Reason,
            injury.StartedOn, injury.ExpectedReturnOn, injury.ReturnedOn, injury.IsActive, injury.DaysOut(today),
            (documents ?? []).OrderByDescending(d => d.UploadedAt).Select(ToDto).ToList());
    }

    public static InjuryDocumentDto ToDto(InjuryDocument document) => new(
        document.Id, document.Kind, KindLabel(document.Kind), document.FileName, document.ContentType, document.SizeBytes, document.Note,
        document.UploadedAt, $"/api/squads/{document.SquadId}/injuries/{document.InjuryId}/documents/{document.Id}");

    public static string KindLabel(InjuryDocumentKind kind) => kind switch
    {
        InjuryDocumentKind.Imaging => "Radiografía o prueba de imagen",
        InjuryDocumentKind.MedicalReport => "Informe médico",
        _ => "Otro documento",
    };

    public static IReadOnlyList<InjuryDto> ToList(IEnumerable<PlayerInjury> injuries, Squad squad, DateOnly today, ILookup<Guid, InjuryDocument>? documents = null)
    {
        var players = squad.Players.ToDictionary(p => p.Id);
        return injuries
            .OrderByDescending(i => i.IsActive)
            .ThenByDescending(i => i.StartedOn)
            .Select(i => ToDto(i, players.GetValueOrDefault(i.SquadPlayerId), today, documents?[i.Id]))
            .ToList();
    }

    public static IReadOnlyDictionary<Guid, InjuryDto> ActiveByPlayer(IEnumerable<PlayerInjury> injuries, Squad squad, DateOnly today)
    {
        var players = squad.Players.ToDictionary(p => p.Id);
        return injuries
            .Where(i => i.IsActive)
            .GroupBy(i => i.SquadPlayerId)
            .ToDictionary(g => g.Key, g => ToDto(g.OrderByDescending(i => i.StartedOn).First(), players.GetValueOrDefault(g.Key), today));
    }
}
