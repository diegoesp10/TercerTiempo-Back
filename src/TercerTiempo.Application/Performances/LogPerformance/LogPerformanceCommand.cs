using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;

namespace TercerTiempo.Application.Performances.LogPerformance;

/// <summary>Registra (o actualiza) el rendimiento del jugador autenticado en un partido ya disputado.</summary>
public sealed record LogPerformanceCommand(
    Guid MatchId,
    bool Started,
    int MinutesPlayed,
    int Goals,
    int Assists,
    int Shots,
    int ShotsOnTarget,
    int KeyPasses,
    int Tackles,
    int Interceptions,
    int Saves,
    int YellowCards,
    bool RedCard,
    int SelfRating,
    string? Notes) : ICommand<PerformanceDto>
{
    public PerformanceStats ToStats() => new(
        Started, MinutesPlayed, Goals, Assists, Shots, ShotsOnTarget, KeyPasses,
        Tackles, Interceptions, Saves, YellowCards, RedCard, SelfRating, Notes);
}

internal sealed class LogPerformanceValidator : AbstractValidator<LogPerformanceCommand>
{
    public LogPerformanceValidator()
    {
        RuleFor(x => x.MatchId).NotEmpty();
        RuleFor(x => x.MinutesPlayed).InclusiveBetween(0, PerformanceStats.MaxMinutes);
        RuleFor(x => x.Goals).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Assists).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Shots).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ShotsOnTarget).GreaterThanOrEqualTo(0).LessThanOrEqualTo(x => x.Shots)
            .WithMessage("Los tiros a puerta no pueden superar los tiros totales.");
        RuleFor(x => x.KeyPasses).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Tackles).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Interceptions).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Saves).GreaterThanOrEqualTo(0);
        RuleFor(x => x.YellowCards).InclusiveBetween(0, 2);
        RuleFor(x => x.SelfRating).InclusiveBetween(1, 10);
        RuleFor(x => x.Notes).MaximumLength(PerformanceStats.MaxNotesLength);
    }
}

internal sealed class LogPerformanceHandler(
    ICurrentUser currentUser,
    IPlayerStore players,
    IMatchStore matches,
    IPerformanceStore performances,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<LogPerformanceCommand, PerformanceDto>
{
    public async Task<PerformanceDto> Handle(LogPerformanceCommand command, CancellationToken cancellationToken)
    {
        var player = await Resolvers.CurrentPlayerAsync(currentUser, players, cancellationToken);

        var match = await matches.GetByIdAsync(command.MatchId, cancellationToken)
            ?? throw new NotFoundException("Partido", command.MatchId);

        var membership = player.CurrentMembership
            ?? throw new ForbiddenException("No perteneces a ninguna plantilla.");

        if (membership.SeasonId != match.SeasonId)
            throw new ForbiddenException("Este partido no es de la competición de tu equipo.");

        var now = clock.GetUtcNow();
        var stats = command.ToStats();

        var performance = await performances.GetByPlayerAndMatchAsync(player.Id, match.Id, cancellationToken);

        if (performance is null)
        {
            performance = PlayerMatchPerformance.Log(player, match, membership.TeamId, stats, now);
            performances.Add(performance);
        }
        else
        {
            PerformancePolicy.EnsureCanLog(player, match, performance.TeamId);
            performance.Update(stats, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return PerformanceMapper.ToDto(performance, player);
    }
}
