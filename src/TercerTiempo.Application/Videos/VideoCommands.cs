using FluentValidation;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Videos;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Videos;

public sealed record UploadMatchVideoCommand(Guid SquadId, Guid MatchId, VideoPart Part, string? Title, VideoUpload File) : ICommand<MatchVideoDto>;

internal sealed class UploadMatchVideoValidator : AbstractValidator<UploadMatchVideoCommand>
{
    public UploadMatchVideoValidator(IOptions<VideoOptions> options)
    {
        var settings = options.Value;

        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.MatchId).NotEmpty();
        RuleFor(x => x.Part).IsInEnum();
        RuleFor(x => x.Title).MaximumLength(150);
        RuleFor(x => x.File).NotNull();
        RuleFor(x => x.File.FileName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.File.ContentType)
            .Must(ct => settings.AllowedContentTypes.Contains(ct, StringComparer.OrdinalIgnoreCase))
            .WithCatalogMessage(ErrorCode.VideoFormatNotAllowed, string.Join(", ", settings.AllowedContentTypes));
        RuleFor(x => x.File.Length)
            .GreaterThanOrEqualTo(MatchVideo.MinSizeBytes).WithCatalogMessage(ErrorCode.FileEmpty)
            .LessThanOrEqualTo(settings.MaxSizeBytes).WithCatalogMessage(ErrorCode.VideoTooLarge, settings.MaxSizeMb);
    }
}

internal sealed class UploadMatchVideoHandler(
    ICurrentUser currentUser,
    ICoachStore coaches,
    IClubStore clubs,
    ISquadStore squads,
    IMatchStore matches,
    ITeamStore teams,
    IMatchVideoStore videos,
    IVideoStorage storage,
    IOptions<VideoOptions> options,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<UploadMatchVideoCommand, MatchVideoDto>
{
    public async Task<MatchVideoDto> Handle(UploadMatchVideoCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var uploader = await VideoManagers.CurrentAsync(currentUser, coaches, clubs, cancellationToken);

        var match = await matches.GetByIdAsync(command.MatchId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.MatchNotFound, command.MatchId);

        if (match.SeasonId != squad.SeasonId || !match.Involves(squad.TeamId))
            throw new ConflictException(ErrorCode.MatchNotOfSquad);

        await VideoUploads.EnsurePartAvailableAsync(squad, match.Id, command.Part, videos, cancellationToken);

        var storageKey = await storage.SaveAsync(command.File.Content, command.File.FileName, cancellationToken);

        var video = MatchVideo.Upload(
            squad.Id, squad.TeamId, match.Id, match.SeasonId, uploader.CoachId, command.Part,
            command.Title, command.File.FileName, command.File.ContentType, command.File.Length, storageKey,
            options.Value.DefaultPriceCents, clock.GetUtcNow(), uploader.ClubId);

        videos.Add(video);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var team = await teams.GetByIdAsync(squad.TeamId, cancellationToken);
        return VideoMapper.ToDto(video, team?.Name ?? "Equipo", uploader.Name, VideoAccessLevel.Owner, canDelete: true);
    }
}

public sealed record PurchaseVideoCommand(Guid VideoId) : ICommand<MatchVideoDto>;

internal sealed class PurchaseVideoValidator : AbstractValidator<PurchaseVideoCommand>
{
    public PurchaseVideoValidator() => RuleFor(x => x.VideoId).NotEmpty();
}

internal sealed class PurchaseVideoHandler(
    ICurrentUser currentUser,
    ICoachStore coaches,
    IClubStore clubs,
    ISquadStore squads,
    ITeamStore teams,
    IMatchVideoStore videos,
    IVideoPurchaseStore purchases,
    IPaymentGateway payments,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<PurchaseVideoCommand, MatchVideoDto>
{
    public async Task<MatchVideoDto> Handle(PurchaseVideoCommand command, CancellationToken cancellationToken)
    {
        var coach = await Resolvers.CurrentCoachAsync(currentUser, coaches, cancellationToken);

        var video = await videos.GetByIdAsync(command.VideoId, cancellationToken);
        if (video is null || video.IsDeleted)
            throw new NotFoundException(ErrorCode.VideoNotFound, command.VideoId);

        var ownerSquad = await squads.GetByIdAsync(video.SquadId, cancellationToken);
        var purchased = await VideoAccess.PurchasedAsync(video, coach.Id, videos, purchases, cancellationToken);
        var level = VideoAccess.LevelFor(video, ownerSquad, currentUser, purchased, clock);

        if (level != VideoAccessLevel.Locked)
            throw level == VideoAccessLevel.Owner ? new ConflictException(ErrorCode.VideoOwnerAccess) : new ConflictException(ErrorCode.VideoAlreadyPurchased);

        var team = await teams.GetByIdAsync(video.TeamId, cancellationToken);
        var payment = await payments.ChargeAsync(
            new PaymentRequest(currentUser.UserId!.Value, video.PriceCents, video.Currency, $"Vídeo «{video.Title}» de {team?.Name ?? "equipo"}"),
            cancellationToken);

        if (!payment.Succeeded)
            throw payment.Error is { } paymentError ? new ConflictException(ErrorCode.VideoPaymentRejected, paymentError) : new ConflictException(ErrorCode.VideoPaymentFailed);

        purchases.Add(VideoPurchase.Create(video.Id, coach.Id, video.PriceCents, video.Currency, payment.Reference, clock.GetUtcNow()));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var uploader = await VideoManagers.UploaderNameAsync(video, coaches, clubs, cancellationToken);
        return VideoMapper.ToDto(video, team?.Name ?? "Equipo", uploader, VideoAccessLevel.Purchased, canDelete: false);
    }
}

public sealed record DeleteVideoCommand(Guid VideoId) : ICommand<bool>;

internal sealed class DeleteVideoValidator : AbstractValidator<DeleteVideoCommand>
{
    public DeleteVideoValidator() => RuleFor(x => x.VideoId).NotEmpty();
}

internal sealed class DeleteVideoHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IClubStore clubs,
    IMatchVideoStore videos,
    IVideoStorage storage,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<DeleteVideoCommand, bool>
{
    public async Task<bool> Handle(DeleteVideoCommand command, CancellationToken cancellationToken)
    {
        var video = await videos.GetByIdAsync(command.VideoId, cancellationToken);
        if (video is null || video.IsDeleted)
            throw new NotFoundException(ErrorCode.VideoNotFound, command.VideoId);

        await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, video.SquadId, clock, cancellationToken);

        video.Delete(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await storage.DeleteAsync(video.StorageKey, cancellationToken);

        return true;
    }
}

public sealed record IssueVideoTicketCommand(Guid VideoId) : ICommand<VideoTicketDto>;

internal sealed class IssueVideoTicketValidator : AbstractValidator<IssueVideoTicketCommand>
{
    public IssueVideoTicketValidator() => RuleFor(x => x.VideoId).NotEmpty();
}

internal sealed class IssueVideoTicketHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IClubStore clubs,
    IMatchVideoStore videos,
    IVideoPurchaseStore purchases,
    IVideoTickets tickets,
    TimeProvider clock) : ICommandHandler<IssueVideoTicketCommand, VideoTicketDto>
{
    public async Task<VideoTicketDto> Handle(IssueVideoTicketCommand command, CancellationToken cancellationToken)
    {
        var video = await videos.GetByIdAsync(command.VideoId, cancellationToken);
        if (video is null || video.IsDeleted)
            throw new NotFoundException(ErrorCode.VideoNotFound, command.VideoId);

        if (!video.IsPublic && (!currentUser.IsAuthenticated || currentUser.UserId is null))
            throw new UnauthenticatedException();

        var ownerSquad = await squads.GetByIdAsync(video.SquadId, cancellationToken);
        var purchased = await VideoAccess.PurchasedAsync(video, currentUser.CoachId, videos, purchases, cancellationToken);
        var clubOwner = ownerSquad is not null && await SquadAccess.IsClubOfAsync(ownerSquad, currentUser, clubs, cancellationToken);
        var level = VideoAccess.LevelFor(video, ownerSquad, currentUser, purchased, clock, clubOwner);

        if (!VideoAccess.CanWatch(level))
            throw level == VideoAccessLevel.Locked ? new ForbiddenException(ErrorCode.VideoLocked) : new ForbiddenException(ErrorCode.VideoNotAllowed);

        var (ticket, expiresAt) = tickets.Issue(video.Id, currentUser.UserId ?? Guid.Empty, level == VideoAccessLevel.Free && video.IsPublic);
        return new VideoTicketDto(video.Id, ticket, expiresAt);
    }
}
