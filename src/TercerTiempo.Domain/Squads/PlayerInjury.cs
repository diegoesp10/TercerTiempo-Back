using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Squads;

public enum InjuryType
{
    MuscleContracture = 1,
    MuscleTear = 2,
    AnkleSprain = 3,
    KneeSprain = 4,
    CruciateLigament = 5,
    Meniscus = 6,
    Fracture = 7,
    Contusion = 8,
    Tendinitis = 9,
    Pubalgia = 10,
    Concussion = 11,
    Illness = 12,
    Other = 99,
}

public sealed record InjuryTypeInfo(InjuryType Type, string Label, int? AverageDays);

// Duración media orientativa de cada tipo de lesión: sirve para proponer la fecha de vuelta al registrarla.
public static class InjuryCatalog
{
    public static readonly IReadOnlyList<InjuryTypeInfo> Types =
    [
        new(InjuryType.MuscleContracture, "Contractura muscular", 7),
        new(InjuryType.MuscleTear, "Rotura fibrilar", 21),
        new(InjuryType.AnkleSprain, "Esguince de tobillo", 21),
        new(InjuryType.KneeSprain, "Esguince de rodilla", 28),
        new(InjuryType.CruciateLigament, "Ligamento cruzado", 240),
        new(InjuryType.Meniscus, "Menisco", 60),
        new(InjuryType.Fracture, "Fractura", 60),
        new(InjuryType.Contusion, "Contusión / golpe", 5),
        new(InjuryType.Tendinitis, "Tendinitis", 21),
        new(InjuryType.Pubalgia, "Pubalgia", 42),
        new(InjuryType.Concussion, "Conmoción", 10),
        new(InjuryType.Illness, "Enfermedad", 5),
        new(InjuryType.Other, "Otra", null),
    ];

    public static InjuryTypeInfo Info(InjuryType type) => Types.FirstOrDefault(t => t.Type == type) ?? Types[^1];
}

public sealed class PlayerInjury : AggregateRoot
{
    public Guid SquadId { get; private set; }
    public Guid SquadPlayerId { get; private set; }
    public InjuryType Type { get; private set; }
    public string? Reason { get; private set; }
    public DateOnly StartedOn { get; private set; }
    public DateOnly? ExpectedReturnOn { get; private set; }
    public DateOnly? ReturnedOn { get; private set; }
    public Guid? RecordedByCoachId { get; private set; }
    public Guid? RecordedByClubId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsActive => ReturnedOn is null;

    private PlayerInjury() { }

    public static PlayerInjury Create(
        Squad squad, SquadPlayer player, SquadEditor editor, InjuryType type, string? reason, DateOnly startedOn, DateOnly? expectedReturnOn, DateTimeOffset now)
    {
        if (player.SquadId != squad.Id || !player.IsActive)
            throw new DomainException(ErrorCode.SquadPlayerNotMember);

        var injury = new PlayerInjury
        {
            Id = NewId(),
            SquadId = squad.Id,
            SquadPlayerId = player.Id,
            RecordedByCoachId = editor.CoachId,
            RecordedByClubId = editor.ClubId,
        };
        injury.Update(type, reason, startedOn, expectedReturnOn, now);
        return injury;
    }

    public void Update(InjuryType type, string? reason, DateOnly startedOn, DateOnly? expectedReturnOn, DateTimeOffset now)
    {
        if (!Enum.IsDefined(type))
            throw new DomainException(ErrorCode.InjuryInvalidType);

        if (expectedReturnOn is { } expected && expected < startedOn)
            throw new DomainException(ErrorCode.InjuryDatesInvalid);

        if (ReturnedOn is { } returned && returned < startedOn)
            throw new DomainException(ErrorCode.InjuryReturnInvalid);

        Type = type;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : Guard.NotEmpty(reason, "motivo", 300);
        StartedOn = startedOn;
        ExpectedReturnOn = expectedReturnOn;
        UpdatedAt = now;
    }

    public void Close(DateOnly returnedOn, DateTimeOffset now)
    {
        if (!IsActive)
            throw new DomainException(ErrorCode.InjuryAlreadyClosed);

        if (returnedOn < StartedOn)
            throw new DomainException(ErrorCode.InjuryReturnInvalid);

        ReturnedOn = returnedOn;
        UpdatedAt = now;
    }

    public void Reopen(DateTimeOffset now)
    {
        ReturnedOn = null;
        UpdatedAt = now;
    }

    public int DaysOut(DateOnly today) => Math.Max(0, (ReturnedOn ?? today).DayNumber - StartedOn.DayNumber);
}
