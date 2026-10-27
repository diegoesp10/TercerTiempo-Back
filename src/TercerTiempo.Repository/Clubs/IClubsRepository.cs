using System.ComponentModel;
using TercerTiempo.Application.Clubs;
using TercerTiempo.Application.Clubs.Appointments;
using TercerTiempo.Application.Clubs.Catalog;
using TercerTiempo.Application.Clubs.ManageTeams;
using TercerTiempo.Application.Clubs.Roster;
using TercerTiempo.Application.Teams;
using TercerTiempo.Domain.Players;
using TercerTiempo.Application.Teams.ImportCrest;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Repository.Clubs;

public interface IClubsRepository
{
    Task<IReadOnlyList<ClubCatalogDto>> GetCatalogAsync(string? search, CancellationToken cancellationToken = default);
    Task<ClubDto> GetMineAsync(CancellationToken cancellationToken = default);
    Task<ClubDto> AddTeamAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<ClubDto> RemoveTeamAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<ClubSquadDto> OpenSquadAsync(Guid teamId, OpenClubSquadRequest request, CancellationToken cancellationToken = default);
    Task<TeamCrestImportDto> UploadTeamCrestAsync(Guid teamId, CrestUpload file, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RosterEntryDto>> AddRosterEntryAsync(Guid teamId, RosterEntryRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RosterEntryDto>> UpdateRosterEntryAsync(Guid teamId, Guid entryId, RosterEntryRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RosterEntryDto>> RemoveRosterEntryAsync(Guid teamId, Guid entryId, CancellationToken cancellationToken = default);
    Task<RosterEntryDetailDto> GetRosterEntryAsync(Guid teamId, Guid entryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RosterEntryDto>> UploadRosterPhotoAsync(Guid teamId, Guid entryId, CrestUpload file, CancellationToken cancellationToken = default);
    Task<AppointmentDto> CreateAppointmentAsync(CreateAppointmentRequest request, CancellationToken cancellationToken = default);
    Task<AppointmentDto> UpdateAppointmentAsync(Guid appointmentId, UpdateAppointmentRequest request, CancellationToken cancellationToken = default);
    Task<AppointmentDto> RevokeAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken = default);
}

public sealed record RosterEntryRequest(
    [property: Description("Nombre y apellidos.")]
    string FullName,
    [property: Description("Goalkeeper, Defender, Midfielder, Forward o vacío.")]
    Position? Position,
    [property: Description("1-99, opcional.")]
    int? ShirtNumber);

public sealed record OpenClubSquadRequest(
    [property: Description("Partido desde el que se abre: la plantilla es la de la temporada de ese partido. Vacío = la temporada que se está viendo.")]
    Guid? MatchId);

public sealed record ClubTeamRequest(
    [property: Description("Id del equipo (GET /api/teams?competitionId=...).")]
    Guid TeamId);

public sealed record CreateAppointmentRequest(
    [property: Description("Equipo del club.")]
    Guid TeamId,
    [property: Description("Nombre del técnico.")]
    string CoachName,
    [property: Description("Email del técnico (opcional).")]
    string? Email,
    [property: Description("Rol: HeadCoach, AssistantCoach, Delegate, Physio, Analyst u Other.")]
    StaffRole Role,
    [property: Description("Último día de validez (yyyy-MM-dd). Vacío = hasta el final de la temporada.")]
    DateOnly? ValidUntil)
{
    internal CreateAppointmentCommand ToCommand() => new(TeamId, CoachName, Email, Role, ValidUntil);
}

public sealed record UpdateAppointmentRequest(
    [property: Description("Nuevo último día de validez (yyyy-MM-dd), sin pasar del final de la temporada.")]
    DateOnly ValidUntil);
