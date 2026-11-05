using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Squads;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Trainings;

namespace TercerTiempo.Application.Trainings;

// Jugadores que vienen a entrenar a prueba, sin ficha: el cuerpo técnico los apunta, los mete en las sesiones a las que
// vienen y, si les hace ficha, pasan a la plantilla con su historial de asistencia.

public sealed record ListTrialPlayersQuery(Guid SquadId) : IQuery<IReadOnlyList<TrialPlayerDto>>;

public sealed record CreateTrialPlayerCommand(Guid SquadId, string FullName, string? Notes) : ICommand<TrialPlayerDto>;

public sealed record UpdateTrialPlayerCommand(Guid SquadId, Guid TrialPlayerId, string FullName, string? Notes) : ICommand<TrialPlayerDto>;

public sealed record DeleteTrialPlayerCommand(Guid SquadId, Guid TrialPlayerId) : ICommand<bool>;

public sealed record PromoteTrialPlayerCommand(
    Guid SquadId,
    Guid TrialPlayerId,
    [property: Description("Opcional: Goalkeeper, Defender, Midfielder o Forward.")]
    Position? Position,
    [property: Description("Opcional, 1-99.")]
    int? ShirtNumber) : ICommand<SquadPlayerDto>;

// Apunta a un jugador de prueba a una sesión: uno ya conocido (TrialPlayerId) o uno nuevo por su nombre. Si ya hay uno
// a prueba con ese nombre, se reutiliza.
public sealed record AddTrialPlayerToTrainingCommand(Guid SquadId, Guid SessionId, Guid? TrialPlayerId, string? FullName, string? Notes) : ICommand<TrainingSessionDto>;

public sealed record RemoveTrialPlayerFromTrainingCommand(Guid SquadId, Guid SessionId, Guid TrialPlayerId) : ICommand<TrainingSessionDto>;

internal sealed class CreateTrialPlayerValidator : AbstractValidator<CreateTrialPlayerCommand>
{
    public CreateTrialPlayerValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

internal sealed class UpdateTrialPlayerValidator : AbstractValidator<UpdateTrialPlayerCommand>
{
    public UpdateTrialPlayerValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.TrialPlayerId).NotEmpty();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

internal sealed class PromoteTrialPlayerValidator : AbstractValidator<PromoteTrialPlayerCommand>
{
    public PromoteTrialPlayerValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.TrialPlayerId).NotEmpty();
        RuleFor(x => x.Position).IsInEnum().When(x => x.Position.HasValue);
        RuleFor(x => x.ShirtNumber).InclusiveBetween(1, 99).When(x => x.ShirtNumber.HasValue);
    }
}

internal sealed class AddTrialPlayerToTrainingValidator : AbstractValidator<AddTrialPlayerToTrainingCommand>
{
    public AddTrialPlayerToTrainingValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150).When(x => x.TrialPlayerId is null || x.TrialPlayerId == Guid.Empty);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

internal static class TrialPlayers
{
    public static async Task<TrialPlayer> OfSquadAsync(Squad squad, Guid trialPlayerId, ITrialPlayerStore trials, CancellationToken cancellationToken)
    {
        var trial = await trials.GetByIdAsync(trialPlayerId, cancellationToken);
        if (trial is null || trial.SquadId != squad.Id)
            throw new NotFoundException(ErrorCode.TrialPlayerNotFound, trialPlayerId);
        return trial;
    }

    public static async Task<IReadOnlyList<TrialPlayerDto>> ListAsync(Squad squad, ITrialPlayerStore trials, ITrainingStore trainings, CancellationToken cancellationToken)
    {
        var counts = await trainings.CountTrialAttendanceAsync(squad.Id, cancellationToken);
        return (await trials.GetBySquadAsync(squad.Id, cancellationToken))
            .Select(t => TrainingMapper.ToDto(t, counts.GetValueOrDefault(t.Id)))
            .ToList();
    }
}

internal sealed class ListTrialPlayersHandler(ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, ITrialPlayerStore trials, ITrainingStore trainings, TimeProvider clock)
    : IQueryHandler<ListTrialPlayersQuery, IReadOnlyList<TrialPlayerDto>>
{
    public async Task<IReadOnlyList<TrialPlayerDto>> Handle(ListTrialPlayersQuery query, CancellationToken cancellationToken)
    {
        var squad = await TrainingAccess.SquadForViewerAsync(squads, clubs, currentUser, query.SquadId, clock, cancellationToken);
        return await TrialPlayers.ListAsync(squad, trials, trainings, cancellationToken);
    }
}

internal sealed class CreateTrialPlayerHandler(
    ICurrentUser currentUser, ISquadStore squads, ICoachStore coaches, IClubStore clubs, ITrialPlayerStore trials, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<CreateTrialPlayerCommand, TrialPlayerDto>
{
    public async Task<TrialPlayerDto> Handle(CreateTrialPlayerCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var editor = await Resolvers.CurrentEditorAsync(currentUser, coaches, clubs, cancellationToken);

        if ((await trials.GetBySquadAsync(squad.Id, cancellationToken)).Any(t => !t.IsPromoted && t.Matches(command.FullName)))
            throw new ConflictException(ErrorCode.TrialPlayerDuplicate, command.FullName.Trim());

        var trial = TrialPlayer.Create(squad, editor, command.FullName, command.Notes, clock.GetUtcNow());
        trials.Add(trial);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TrainingMapper.ToDto(trial, null);
    }
}

internal sealed class UpdateTrialPlayerHandler(
    ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, ITrialPlayerStore trials, ITrainingStore trainings, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<UpdateTrialPlayerCommand, TrialPlayerDto>
{
    public async Task<TrialPlayerDto> Handle(UpdateTrialPlayerCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var trial = await TrialPlayers.OfSquadAsync(squad, command.TrialPlayerId, trials, cancellationToken);

        if ((await trials.GetBySquadAsync(squad.Id, cancellationToken)).Any(t => t.Id != trial.Id && !t.IsPromoted && t.Matches(command.FullName)))
            throw new ConflictException(ErrorCode.TrialPlayerDuplicate, command.FullName.Trim());

        trial.Update(command.FullName, command.Notes, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var counts = await trainings.CountTrialAttendanceAsync(squad.Id, cancellationToken);
        return TrainingMapper.ToDto(trial, counts.GetValueOrDefault(trial.Id));
    }
}

// Se borra con sus líneas de asistencia; el que ya tiene ficha no se borra (su historial ya es del jugador).
internal sealed class DeleteTrialPlayerHandler(
    ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, ITrialPlayerStore trials, ITrainingStore trainings, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<DeleteTrialPlayerCommand, bool>
{
    public async Task<bool> Handle(DeleteTrialPlayerCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var trial = await TrialPlayers.OfSquadAsync(squad, command.TrialPlayerId, trials, cancellationToken);
        if (trial.IsPromoted)
            throw new ConflictException(ErrorCode.TrialPlayerPromoted, trial.FullName);

        var now = clock.GetUtcNow();
        foreach (var session in await trainings.GetByTrialPlayerAsync(trial.Id, cancellationToken))
            session.RemoveTrialPlayer(trial.Id, now);
        trials.Remove(trial);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}

internal sealed class PromoteTrialPlayerHandler(
    ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, ITrialPlayerStore trials, ITrainingStore trainings, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<PromoteTrialPlayerCommand, SquadPlayerDto>
{
    public async Task<SquadPlayerDto> Handle(PromoteTrialPlayerCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var trial = await TrialPlayers.OfSquadAsync(squad, command.TrialPlayerId, trials, cancellationToken);
        var now = clock.GetUtcNow();

        var player = trial.Promote(squad, command.Position, command.ShirtNumber, now);
        // Lo que ya se le había anotado en los entrenamientos pasa a su ficha.
        foreach (var session in await trainings.GetByTrialPlayerAsync(trial.Id, cancellationToken))
            session.PromoteTrialPlayer(trial.Id, player.Id, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return SquadMapper.ToPlayerDto(player, null);
    }
}

internal sealed class AddTrialPlayerToTrainingHandler(
    ICurrentUser currentUser, ISquadStore squads, ICoachStore coaches, IClubStore clubs, ITrainingStore trainings, ITrialPlayerStore trials, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<AddTrialPlayerToTrainingCommand, TrainingSessionDto>
{
    public async Task<TrainingSessionDto> Handle(AddTrialPlayerToTrainingCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var session = await TrainingAccess.SessionOfAsync(squad, command.SessionId, trainings, cancellationToken);
        var now = clock.GetUtcNow();

        TrialPlayer trial;
        if (command.TrialPlayerId is { } id && id != Guid.Empty)
        {
            trial = await TrialPlayers.OfSquadAsync(squad, id, trials, cancellationToken);
        }
        else
        {
            var name = command.FullName?.Trim();
            if (string.IsNullOrEmpty(name))
                throw new DomainException(ErrorCode.TrialPlayerNotFound, Guid.Empty);
            var known = (await trials.GetBySquadAsync(squad.Id, cancellationToken)).FirstOrDefault(t => !t.IsPromoted && t.Matches(name));
            if (known is null)
            {
                var editor = await Resolvers.CurrentEditorAsync(currentUser, coaches, clubs, cancellationToken);
                known = TrialPlayer.Create(squad, editor, name, command.Notes, now);
                trials.Add(known);
            }
            trial = known;
        }

        session.AddTrialPlayer(trial, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await TrainingAccess.DetailAsync(session, squad, currentUser, clock, clubs, trials, cancellationToken);
    }
}

internal sealed class RemoveTrialPlayerFromTrainingHandler(
    ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, ITrainingStore trainings, ITrialPlayerStore trials, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<RemoveTrialPlayerFromTrainingCommand, TrainingSessionDto>
{
    public async Task<TrainingSessionDto> Handle(RemoveTrialPlayerFromTrainingCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var session = await TrainingAccess.SessionOfAsync(squad, command.SessionId, trainings, cancellationToken);
        session.RemoveTrialPlayer(command.TrialPlayerId, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await TrainingAccess.DetailAsync(session, squad, currentUser, clock, clubs, trials, cancellationToken);
    }
}
