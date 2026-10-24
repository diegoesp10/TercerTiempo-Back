using System.ComponentModel;
using FluentValidation;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Clubs.Invitations;

public sealed class RegistrationOptions
{
    public const string SectionName = "Registration";

    // Beta cerrada: un club solo puede darse de alta con una invitación del administrador (técnicos y jugadores ya
    // entran siempre con el código que les da su club).
    public bool ClubInviteRequired { get; set; }
}

public sealed record RegistrationOptionsDto(bool ClubInviteRequired);

public sealed record GetRegistrationOptionsQuery : IQuery<RegistrationOptionsDto>;

internal sealed class GetRegistrationOptionsHandler(IOptions<RegistrationOptions> options) : IQueryHandler<GetRegistrationOptionsQuery, RegistrationOptionsDto>
{
    public Task<RegistrationOptionsDto> Handle(GetRegistrationOptionsQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(new RegistrationOptionsDto(options.Value.ClubInviteRequired));
}

public sealed record ClubInvitationDto(
    Guid Id,
    string Code,
    string? Note,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? UsedAt,
    string? ClubName,
    DateTimeOffset? RevokedAt);

internal static class ClubInvitationMapper
{
    public static ClubInvitationDto ToDto(ClubInvitation invitation, string? clubName, DateTimeOffset now) => new(
        invitation.Id,
        invitation.Code,
        invitation.Note,
        invitation.UsedAt is not null ? "used" : invitation.RevokedAt is not null ? "revoked" : invitation.IsUsable(now) ? "available" : "expired",
        invitation.CreatedAt,
        invitation.ExpiresAt,
        invitation.UsedAt,
        clubName,
        invitation.RevokedAt);
}

public sealed record ListClubInvitationsQuery : IQuery<IReadOnlyList<ClubInvitationDto>>;

internal sealed class ListClubInvitationsHandler(IClubInvitationStore invitations, IClubStore clubs, TimeProvider clock)
    : IQueryHandler<ListClubInvitationsQuery, IReadOnlyList<ClubInvitationDto>>
{
    public async Task<IReadOnlyList<ClubInvitationDto>> Handle(ListClubInvitationsQuery query, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var result = new List<ClubInvitationDto>();
        foreach (var invitation in await invitations.GetAllAsync(cancellationToken))
        {
            var club = invitation.ClubId is { } clubId ? await clubs.GetByIdAsync(clubId, cancellationToken) : null;
            result.Add(ClubInvitationMapper.ToDto(invitation, club?.Name, now));
        }
        return result;
    }
}

public sealed record CreateClubInvitationCommand(
    [property: Description("Para quién es (p. ej. el nombre del club). Solo lo ve el administrador.")]
    string? Note,
    [property: Description("Días que dura la invitación; sin valor, no caduca.")]
    int? ExpiresInDays) : ICommand<ClubInvitationDto>;

internal sealed class CreateClubInvitationValidator : AbstractValidator<CreateClubInvitationCommand>
{
    public CreateClubInvitationValidator()
    {
        RuleFor(x => x.Note).MaximumLength(200);
        RuleFor(x => x.ExpiresInDays).InclusiveBetween(1, 365).When(x => x.ExpiresInDays.HasValue);
    }
}

internal sealed class CreateClubInvitationHandler(IClubInvitationStore invitations, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<CreateClubInvitationCommand, ClubInvitationDto>
{
    public async Task<ClubInvitationDto> Handle(CreateClubInvitationCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var invitation = ClubInvitation.Create(command.Note?.Trim(), command.ExpiresInDays is { } days ? now.AddDays(days) : null, now);
        invitations.Add(invitation);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ClubInvitationMapper.ToDto(invitation, null, now);
    }
}

public sealed record RevokeClubInvitationCommand(Guid Id) : ICommand<ClubInvitationDto>;

internal sealed class RevokeClubInvitationHandler(IClubInvitationStore invitations, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<RevokeClubInvitationCommand, ClubInvitationDto>
{
    public async Task<ClubInvitationDto> Handle(RevokeClubInvitationCommand command, CancellationToken cancellationToken)
    {
        var invitation = await invitations.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.ClubInvitationNotFound, command.Id);
        var now = clock.GetUtcNow();
        invitation.Revoke(now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ClubInvitationMapper.ToDto(invitation, null, now);
    }
}
