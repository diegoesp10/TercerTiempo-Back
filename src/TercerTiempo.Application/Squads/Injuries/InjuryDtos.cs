using System.ComponentModel;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Squads.Injuries;

public sealed record InjuryTypeDto(
    [property: Description("Código del tipo en el catálogo (el que se manda al registrar una lesión).")]
    string Type,
    string Label,
    [property: Description("Duración media orientativa en días; null si no hay referencia (Otra).")]
    int? AverageDays);

public sealed record InjuryDto(
    Guid Id,
    Guid SquadPlayerId,
    string PlayerName,
    int? ShirtNumber,
    string Type,
    string TypeLabel,
    int? AverageDays,
    string? Reason,
    [property: Description("Fecha del parte: desde ese día cuentan los días de baja.")]
    DateOnly StartedOn,
    [property: Description("Vuelta prevista ahora (cambia con lo que diga el médico); null si no se sabe.")]
    DateOnly? ExpectedReturnOn,
    [property: Description("Día del alta; null mientras siga lesionado.")]
    DateOnly? ReturnedOn,
    bool Active,
    [property: Description("Días de baja contados desde el parte: hasta hoy si sigue lesionado, hasta el alta si ya la tiene.")]
    int DaysOut,
    [property: Description("Pruebas e informes de la lesión (radiografías, informes del hospital...), los últimos primero. Solo los abren el club, el cuerpo técnico y el propio jugador.")]
    IReadOnlyList<InjuryDocumentDto> Documents,
    [property: Description("El jugador causó baja en la plantilla: la lesión es historial (se consulta y se puede borrar, pero no se cambia) y sus días de baja se cuentan hasta ese día.")]
    bool PlayerLeft = false,
    [property: Description("Cuándo se registró el parte en la app; no cambia al corregir las fechas.")]
    DateTimeOffset? ReportedAt = null,
    [property: Description("La vuelta que se previó al dar el parte, para comparar lo previsto con lo real.")]
    DateOnly? InitialExpectedReturnOn = null,
    [property: Description("Días de baja previstos ahora (del parte a la vuelta prevista).")]
    int? ExpectedDays = null,
    [property: Description("Días de baja previstos al dar el parte.")]
    int? InitialExpectedDays = null,
    [property: Description("Solo si sigue lesionado y hay vuelta prevista: días que faltan para ella (negativo si ya ha pasado).")]
    int? DaysLeft = null,
    [property: Description("Lesión superada de la que esta es recaída; null si no lo es.")]
    Guid? RelapseOfInjuryId = null,
    [property: Description("Viene de la temporada anterior: es la misma lesión, que seguía abierta al cambiar de temporada, y conserva su parte (los días se cuentan desde él).")]
    Guid? ContinuesInjuryId = null,
    [property: Description("Día en que pasó sin el alta a la temporada siguiente: aquí es historial (sus días cuentan hasta entonces) y se lleva desde la plantilla de esa temporada; null si no pasó.")]
    DateOnly? ContinuedOn = null);

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
    public static InjuryTypeDto ToDto(InjuryTypeDefinition type) => new(type.Code, type.Label, type.AverageDays);

    public static InjuryDto ToDto(PlayerInjury injury, SquadPlayer? player, DateOnly today, IEnumerable<InjuryDocument>? documents = null)
    {
        var type = injury.TypeDefinition;
        // De quien causó baja con la lesión abierta, la plantilla solo sabe hasta ese día.
        var until = player?.LeftOn is { } left && left < today ? left : today;
        return new InjuryDto(
            injury.Id, injury.SquadPlayerId, player?.FullName ?? "Jugador", player?.ShirtNumber,
            injury.Type, type.Label, type.AverageDays, injury.Reason,
            injury.StartedOn, injury.ExpectedReturnOn, injury.ReturnedOn, injury.IsActive, injury.DaysOut(until),
            (documents ?? []).OrderByDescending(d => d.UploadedAt).Select(ToDto).ToList(),
            PlayerLeft: player is { IsActive: false },
            ReportedAt: injury.ReportedAt,
            InitialExpectedReturnOn: injury.InitialExpectedReturnOn,
            ExpectedDays: injury.ExpectedDays,
            InitialExpectedDays: injury.InitialExpectedDays,
            DaysLeft: injury.IsActive && injury.ExpectedReturnOn is { } expected ? expected.DayNumber - today.DayNumber : null,
            RelapseOfInjuryId: injury.RelapseOfInjuryId,
            ContinuesInjuryId: injury.ContinuesInjuryId,
            ContinuedOn: injury.ContinuedOn);
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
