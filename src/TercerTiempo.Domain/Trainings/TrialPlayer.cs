using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Domain.Trainings;

// Alguien que viene a entrenar a prueba sin ficha en la plantilla: el cuerpo técnico lo apunta por su nombre, lo mete
// en las sesiones a las que viene y le pasa lista como a los demás. Si le hacen ficha, pasa a ser jugador de la
// plantilla y sus asistencias de prueba se quedan a su nombre.
public sealed class TrialPlayer : AggregateRoot
{
    public Guid SquadId { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public Guid? CreatedByCoachId { get; private set; }
    public Guid? CreatedByClubId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? PromotedSquadPlayerId { get; private set; }
    public DateTimeOffset? PromotedAt { get; private set; }

    public bool IsPromoted => PromotedSquadPlayerId is not null;

    private TrialPlayer() { }

    public static TrialPlayer Create(Squad squad, SquadEditor editor, string fullName, string? notes, DateTimeOffset now)
    {
        var trial = new TrialPlayer
        {
            Id = NewId(),
            SquadId = squad.Id,
            CreatedByCoachId = editor.CoachId,
            CreatedByClubId = editor.ClubId,
            CreatedAt = now,
        };
        trial.Update(fullName, notes, now);
        return trial;
    }

    public void Update(string fullName, string? notes, DateTimeOffset now)
    {
        if (IsPromoted)
            throw new DomainException(ErrorCode.TrialPlayerPromoted, FullName);

        var name = Guard.NotEmpty(fullName, "nombre", 150);
        FullName = name;
        NormalizedName = NameNormalizer.Normalize(name);
        Notes = string.IsNullOrWhiteSpace(notes) ? null : Guard.NotEmpty(notes, "notas", 500);
        UpdatedAt = now;
    }

    public bool Matches(string name) => NormalizedName == NameNormalizer.Normalize(name);

    // Le hacen ficha: se da de alta en la plantilla con su nombre y el jugador de prueba queda cerrado apuntando a ella.
    public SquadPlayer Promote(Squad squad, Position? position, int? shirtNumber, DateTimeOffset now)
    {
        if (IsPromoted)
            throw new DomainException(ErrorCode.TrialPlayerPromoted, FullName);
        if (squad.Id != SquadId)
            throw new DomainException(ErrorCode.TrialPlayerNotFound, Id);

        var player = squad.AddPlayer(FullName, position, shirtNumber, null, now);
        PromotedSquadPlayerId = player.Id;
        PromotedAt = now;
        UpdatedAt = now;
        return player;
    }
}

public interface ITrialPlayerStore
{
    Task<TrialPlayer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    // Los de la plantilla, incluidos los que ya tienen ficha (para enseñar de dónde vino cada uno).
    Task<IReadOnlyList<TrialPlayer>> GetBySquadAsync(Guid squadId, CancellationToken cancellationToken = default);
    void Add(TrialPlayer trialPlayer);
    void Remove(TrialPlayer trialPlayer);
}
