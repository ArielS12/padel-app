using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Padel.Api.Contracts;
using Padel.Api.Data;
using Padel.Api.Domain;

namespace Padel.Api.Controllers;

[Authorize(Roles = "Admin")]
public sealed class AdminController(AppDbContext db) : ApiControllerBase
{
    [HttpGet("clubs/pending")]
    public async Task<ActionResult<IReadOnlyCollection<ClubResponse>>> PendingClubs(CancellationToken cancellationToken)
    {
        var clubs = await db.Clubs
            .Include(club => club.Courts)
            .ThenInclude(court => court.Schedules)
            .Where(club => club.Status == ClubStatus.PendingApproval)
            .OrderBy(club => club.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Ok(clubs.Select(ToResponse).ToList());
    }

    [HttpPost("clubs/{clubId:guid}/approve")]
    public async Task<IActionResult> ApproveClub(Guid clubId, CancellationToken cancellationToken)
    {
        return await SetClubStatus(clubId, ClubStatus.Approved, cancellationToken);
    }

    [HttpPost("clubs/{clubId:guid}/reject")]
    public async Task<IActionResult> RejectClub(Guid clubId, CancellationToken cancellationToken)
    {
        return await SetClubStatus(clubId, ClubStatus.Rejected, cancellationToken);
    }

    [HttpGet("mercadopago")]
    public async Task<ActionResult<MercadoPagoSettingsResponse>> GetMercadoPagoSettings(CancellationToken cancellationToken)
    {
        var settings = await GetOrCreateMercadoPagoSettingsAsync(cancellationToken);
        return Ok(ToResponse(settings));
    }

    [HttpPut("mercadopago")]
    public async Task<ActionResult<MercadoPagoSettingsResponse>> UpdateMercadoPagoSettings(UpdateMercadoPagoSettingsRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.OAuthClientId) &&
            (request.OAuthClientId.StartsWith("APP_USR-", StringComparison.OrdinalIgnoreCase) ||
             request.OAuthClientId.StartsWith("TEST-", StringComparison.OrdinalIgnoreCase)))
        {
            return ValidationProblem("Application ID / Client ID debe ser el N° de aplicación, no la Public Key ni el Access Token.");
        }

        var settings = await GetOrCreateMercadoPagoSettingsAsync(cancellationToken);
        settings.Environment = request.Environment;
        settings.PublicKey = request.PublicKey;
        settings.OAuthClientId = request.OAuthClientId;
        settings.OAuthRedirectUrl = request.OAuthRedirectUrl;
        settings.SuccessUrl = request.SuccessUrl;
        settings.FailureUrl = request.FailureUrl;
        settings.PendingUrl = request.PendingUrl;
        settings.NotificationUrl = request.NotificationUrl;
        settings.UpdatedAtUtc = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(request.AccessToken))
        {
            settings.AccessToken = request.AccessToken;
        }

        if (!string.IsNullOrWhiteSpace(request.OAuthClientSecret))
        {
            settings.OAuthClientSecret = request.OAuthClientSecret;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(settings));
    }

    private async Task<IActionResult> SetClubStatus(Guid clubId, ClubStatus status, CancellationToken cancellationToken)
    {
        var club = await db.Clubs.SingleOrDefaultAsync(x => x.Id == clubId, cancellationToken);
        if (club is null)
        {
            return NotFound();
        }

        club.Status = status;
        db.Notifications.Add(new Notification
        {
            UserId = club.OwnerId,
            Type = NotificationType.CourtApproved,
            Title = status == ClubStatus.Approved ? "Cancha aprobada" : "Cancha rechazada",
            Message = status == ClubStatus.Approved
                ? "Tu cancha ya puede recibir turnos."
                : "El administrador rechazo el registro de tu cancha."
        });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<MercadoPagoSettings> GetOrCreateMercadoPagoSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await db.MercadoPagoSettings.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (settings is not null)
        {
            return settings;
        }

        settings = new MercadoPagoSettings();
        db.MercadoPagoSettings.Add(settings);
        await db.SaveChangesAsync(cancellationToken);
        return settings;
    }

    private static MercadoPagoSettingsResponse ToResponse(MercadoPagoSettings settings)
    {
        return new MercadoPagoSettingsResponse(
            settings.Environment,
            settings.PublicKey,
            settings.OAuthClientId,
            settings.OAuthRedirectUrl,
            !string.IsNullOrWhiteSpace(settings.AccessToken),
            !string.IsNullOrWhiteSpace(settings.OAuthClientSecret),
            settings.SuccessUrl,
            settings.FailureUrl,
            settings.PendingUrl,
            settings.NotificationUrl);
    }

    private static ClubResponse ToResponse(Club club)
    {
        return new ClubResponse(
            club.Id,
            club.Name,
            club.Status,
            club.Address,
            club.City,
            club.CourtCount,
            club.FullMatchPrice,
            club.Owner?.MercadoPagoPublicKey,
            club.Courts
                .OrderBy(court => court.Name)
                .Select(court => new CourtResponse(
                    court.Id,
                    court.Name,
                    court.IsActive,
                    court.IsCovered,
                    court.FloorType,
                    court.WallType,
                    court.FullMatchPrice,
                    court.Schedules
                        .OrderBy(schedule => schedule.DayOfWeek)
                        .ThenBy(schedule => schedule.OpensAt)
                        .Select(schedule => new CourtScheduleResponse(schedule.DayOfWeek, schedule.OpensAt, schedule.ClosesAt, schedule.SlotMinutes))
                        .ToList()))
                .ToList());
    }
}
