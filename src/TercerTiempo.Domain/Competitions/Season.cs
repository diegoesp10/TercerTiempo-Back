using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Competitions;

public sealed class Season : Entity
{
    public Guid CompetitionId { get; private set; }
    public string Label { get; private set; } = string.Empty;
    public DateOnly StartsOn { get; private set; }
    public DateOnly EndsOn { get; private set; }
    public bool IsCurrent { get; private set; }
    public ExternalRef? ExternalRef { get; private set; }

    /// <summary>Competición, grupo y temporada en la federación (asturfutbol.es), cuando ya se han localizado.</summary>
    public ExternalRef? OfficialRef { get; private set; }

    private Season() { }

    internal static Season Create(Guid competitionId, string label, DateOnly startsOn, DateOnly endsOn, ExternalRef? externalRef) =>
        new()
        {
            Id = NewId(),
            CompetitionId = competitionId,
            Label = Guard.NotEmpty(label, "temporada", 20),
            StartsOn = startsOn,
            EndsOn = endsOn,
            ExternalRef = externalRef,
        };

    internal void SetCurrent(bool value) => IsCurrent = value;

    public void LinkOfficial(ExternalRef officialRef) => OfficialRef = officialRef;

    public bool Covers(DateOnly date) => date >= StartsOn && date <= EndsOn;
}
