using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Squads.MatchSheet;

public sealed record MatchSheetEntryInput(
    Guid SquadPlayerId,
    [property: Description("false si no jugó o no fue convocado: se borra su línea si existía.")]
    bool Played,
    bool Started,
    [property: Description("0-120.")]
    int MinutesPlayed,
    int Goals,
    int Assists,
    int Shots,
    [property: Description("No puede superar los tiros totales.")]
    int ShotsOnTarget,
    int KeyPasses,
    int Tackles,
    int Interceptions,
    int Saves,
    [property: Description("0-2; con 2 amarillas hay que marcar la roja.")]
    int YellowCards,
    bool RedCard,
    [property: Description("Opcional, máximo 1000 caracteres.")]
    string? Notes,
    [property: Description("Dorsal con el que jugó este partido si fue distinto del de su ficha (1-99); vacío = el de la ficha.")]
    int? ShirtNumber = null,
    [property: Description("Faltas cometidas.")]
    int Fouls = 0,
    [property: Description("Pérdidas de balón.")]
    int BallLosses = 0,
    [property: Description("Recuperaciones de balón.")]
    int Recoveries = 0,
    [property: Description("Pases completados; no pueden superar los intentados.")]
    int PassesCompleted = 0,
    [property: Description("Pases intentados.")]
    int PassesAttempted = 0)
{
    public PerformanceStats ToStats() => new(
        Started, MinutesPlayed, Goals, Assists, Shots, ShotsOnTarget, KeyPasses,
        Tackles, Interceptions, Saves, YellowCards, RedCard, Notes, Fouls,
        BallLosses, Recoveries, PassesCompleted, PassesAttempted);

    public static MatchSheetEntryInput From(PlayerMatchPerformance p) => new(
        p.SquadPlayerId, true, p.Started, p.MinutesPlayed, p.Goals, p.Assists, p.Shots, p.ShotsOnTarget, p.KeyPasses,
        p.Tackles, p.Interceptions, p.Saves, p.YellowCards, p.RedCard, p.Notes, p.ShirtNumber, p.Fouls,
        p.BallLosses, p.Recoveries, p.PassesCompleted, p.PassesAttempted);
}

public sealed record RecordMatchSheetCommand(Guid SquadId, Guid MatchId, IReadOnlyList<MatchSheetEntryInput> Entries) : ICommand<MatchSheetDto>;

internal sealed class RecordMatchSheetValidator : AbstractValidator<RecordMatchSheetCommand>
{
    public RecordMatchSheetValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.MatchId).NotEmpty();
        RuleFor(x => x.Entries).NotNull();
        RuleForEach(x => x.Entries).ChildRules(entry =>
        {
            entry.RuleFor(e => e.SquadPlayerId).NotEmpty();
            entry.When(e => e.Played, () =>
            {
                entry.RuleFor(e => e.MinutesPlayed).InclusiveBetween(0, PerformanceStats.MaxMinutes);
                entry.RuleFor(e => e.Goals).GreaterThanOrEqualTo(0);
                entry.RuleFor(e => e.Assists).GreaterThanOrEqualTo(0);
                entry.RuleFor(e => e.Shots).GreaterThanOrEqualTo(0);
                entry.RuleFor(e => e.ShotsOnTarget).GreaterThanOrEqualTo(0).LessThanOrEqualTo(e => e.Shots)
                    .WithCatalogMessage(ErrorCode.SheetShotsOnTarget);
                entry.RuleFor(e => e.KeyPasses).GreaterThanOrEqualTo(0);
                entry.RuleFor(e => e.Tackles).GreaterThanOrEqualTo(0);
                entry.RuleFor(e => e.Interceptions).GreaterThanOrEqualTo(0);
                entry.RuleFor(e => e.Saves).GreaterThanOrEqualTo(0);
                entry.RuleFor(e => e.YellowCards).InclusiveBetween(0, 2);
                entry.RuleFor(e => e.Fouls).InclusiveBetween(0, 30);
                entry.RuleFor(e => e.BallLosses).InclusiveBetween(0, 200);
                entry.RuleFor(e => e.Recoveries).InclusiveBetween(0, 200);
                entry.RuleFor(e => e.PassesAttempted).InclusiveBetween(0, 300);
                entry.RuleFor(e => e.PassesCompleted).GreaterThanOrEqualTo(0).LessThanOrEqualTo(e => e.PassesAttempted)
                    .WithCatalogMessage(ErrorCode.SheetPassesCompleted);
                entry.RuleFor(e => e.Notes).MaximumLength(PerformanceStats.MaxNotesLength);
                entry.RuleFor(e => e.ShirtNumber).InclusiveBetween(1, 99).When(e => e.ShirtNumber.HasValue);
            });
        });
    }
}

internal sealed class RecordMatchSheetHandler(
    ICurrentUser currentUser,
    ICoachStore coaches,
    IClubStore clubs,
    ISquadStore squads,
    IMatchStore matches,
    ITeamStore teams,
    ICompetitionStore competitions,
    IPlayerStore players,
    IPerformanceStore performances,
    ITeamMatchStatsStore teamStats,
    MatchSheetWriter writer,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<RecordMatchSheetCommand, MatchSheetDto>
{
    public async Task<MatchSheetDto> Handle(RecordMatchSheetCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var editor = await Resolvers.CurrentEditorAsync(currentUser, coaches, clubs, cancellationToken);

        var match = await matches.GetByIdAsync(command.MatchId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.MatchNotFound, command.MatchId);

        await writer.WriteLinesAsync(squad, match, editor, command.Entries, clock.GetUtcNow(), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await MatchSheetBuilder.BuildAsync(squad, match, currentUser, clock, teams, competitions, players, performances, teamStats, clubs, cancellationToken);
    }
}
