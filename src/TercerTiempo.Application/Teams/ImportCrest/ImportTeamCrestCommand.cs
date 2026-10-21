using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.Teams;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Teams.ImportCrest;

/// <summary>Descarga el escudo de un equipo desde una URL y lo guarda en nuestro almacenamiento (para los que no tienen ficha en la federación).</summary>
public sealed record ImportTeamCrestCommand(
    Guid TeamId,
    [property: Description("URL pública de la imagen del escudo (png, jpg, gif, webp o svg).")]
    string Url) : ICommand<TeamCrestImportDto>;

public sealed record TeamCrestImportDto(Guid TeamId, string TeamName, string CrestUrl, string ContentType, int Bytes);

internal sealed class ImportTeamCrestValidator : AbstractValidator<ImportTeamCrestCommand>
{
    public ImportTeamCrestValidator()
    {
        RuleFor(x => x.TeamId).NotEmpty();
        RuleFor(x => x.Url).NotEmpty().MaximumLength(1000)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            .WithCatalogMessage(ErrorCode.CrestInvalidUrl);
    }
}

internal sealed class ImportTeamCrestHandler(
    ITeamStore teams,
    ICrestDownloader downloader,
    ICrestStorage crests,
    IUnitOfWork unitOfWork) : ICommandHandler<ImportTeamCrestCommand, TeamCrestImportDto>
{
    public async Task<TeamCrestImportDto> Handle(ImportTeamCrestCommand command, CancellationToken cancellationToken)
    {
        var team = await teams.GetByIdAsync(command.TeamId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.TeamNotFound, command.TeamId);

        var image = await downloader.DownloadAsync(command.Url, cancellationToken)
            ?? throw new DomainException(ErrorCode.CrestDownloadFailed);

        team.SetCrest(await crests.SaveAsync(team.Id, image, cancellationToken));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new TeamCrestImportDto(team.Id, team.Name, team.CrestLink()!, image.ContentType, image.Content.Length);
    }
}
