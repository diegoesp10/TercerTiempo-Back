using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Squads;

// Un tipo de lesión del catálogo que mantiene el administrador: su nombre y la duración media orientativa con la que se
// propone la vuelta al registrar una lesión. Un tipo que ya usa alguna lesión no se borra (la base de datos tampoco lo
// deja): se retira, y deja de ofrecerse para las nuevas sin tocar las que ya lo tienen.
public sealed class InjuryTypeDefinition
{
    public const int CodeMaxLength = 30;
    public const int LabelMaxLength = 60;
    public const int MaxAverageDays = 730;

    public string Code { get; private set; } = null!;
    public string Label { get; private set; } = null!;
    public int? AverageDays { get; private set; }
    public int SortOrder { get; private set; }
    public bool Active { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private InjuryTypeDefinition() { }

    public static InjuryTypeDefinition Create(string code, string label, int? averageDays, int sortOrder, DateTimeOffset now)
    {
        var type = new InjuryTypeDefinition { Code = Guard.NotEmpty(code, "código", CodeMaxLength), Active = true };
        type.Update(label, averageDays, sortOrder, active: true, now);
        return type;
    }

    public void Update(string label, int? averageDays, int sortOrder, bool active, DateTimeOffset now)
    {
        if (averageDays is { } days && (days < 1 || days > MaxAverageDays))
            throw new DomainException(ErrorCode.InjuryTypeAverageDaysInvalid, MaxAverageDays);

        Label = Guard.NotEmpty(label, "nombre", LabelMaxLength);
        AverageDays = averageDays;
        SortOrder = sortOrder;
        Active = active;
        UpdatedAt = now;
    }
}

// Los tipos con los que nació el catálogo (antes fijos en el código): sus códigos siguen en las lesiones ya guardadas.
public static class InjuryTypeCodes
{
    public const string MuscleContracture = "MuscleContracture";
    public const string MuscleTear = "MuscleTear";
    public const string AnkleSprain = "AnkleSprain";
    public const string KneeSprain = "KneeSprain";
    public const string CruciateLigament = "CruciateLigament";
    public const string Meniscus = "Meniscus";
    public const string Fracture = "Fracture";
    public const string Contusion = "Contusion";
    public const string Tendinitis = "Tendinitis";
    public const string Pubalgia = "Pubalgia";
    public const string Concussion = "Concussion";
    public const string Illness = "Illness";
    public const string Other = "Other";
}

public interface IInjuryTypeStore
{
    Task<IReadOnlyList<InjuryTypeDefinition>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<InjuryTypeDefinition?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, int>> CountUsageAsync(CancellationToken cancellationToken = default);
    void Add(InjuryTypeDefinition type);
    void Remove(InjuryTypeDefinition type);
}
