using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Padel.Api.Contracts;
using Padel.Api.Data;
using Padel.Api.Domain;
using Padel.Api.Services;

namespace Padel.Api.Controllers;

[Authorize]
public sealed class ClubsController(AppDbContext db, IAvailabilityService availability) : ApiControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyCollection<ClubResponse>>> GetApproved(CancellationToken cancellationToken)
    {
        var clubs = await db.Clubs
            .Include(club => club.Owner)
            .Include(club => club.Courts)
            .ThenInclude(court => court.Schedules)
            .Where(club => club.Status == ClubStatus.Approved)
            .OrderBy(club => club.Name)
            .ToListAsync(cancellationToken);

        return Ok(clubs.Select(ToResponse).ToList());
    }

    [HttpGet("mine")]
    [Authorize(Roles = "ClubOwner,Admin")]
    public async Task<ActionResult<IReadOnlyCollection<ClubResponse>>> Mine(CancellationToken cancellationToken)
    {
        var clubs = await db.Clubs
            .Include(club => club.Owner)
            .Include(club => club.Courts)
            .ThenInclude(court => court.Schedules)
            .Where(club => club.OwnerId == CurrentUserId)
            .OrderByDescending(club => club.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Ok(clubs.Select(ToResponse).ToList());
    }

    [HttpPost]
    [Authorize(Roles = "ClubOwner,Admin")]
    public async Task<ActionResult<ClubResponse>> Register(CreateClubRequest request, CancellationToken cancellationToken)
    {
        var club = new Club
        {
            OwnerId = CurrentUserId,
            Name = request.Name,
            CourtCount = 0,
            PasswordHash = string.Empty,
            Status = ClubStatus.PendingApproval
        };

        db.Clubs.Add(club);
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetApproved), new { id = club.Id }, ToResponse(club));
    }

    [HttpPut("{clubId:guid}/details")]
    [Authorize(Roles = "ClubOwner,Admin")]
    public async Task<ActionResult<ClubResponse>> Complete(Guid clubId, CompleteClubRequest request, CancellationToken cancellationToken)
    {
        var club = await db.Clubs
            .Include(x => x.Courts)
            .ThenInclude(x => x.Schedules)
            .Include(x => x.Schedules)
            .SingleOrDefaultAsync(x => x.Id == clubId && x.OwnerId == CurrentUserId, cancellationToken);

        if (club is null)
        {
            return NotFound();
        }

        if (request.Courts.Count == 0)
        {
            return BadRequest("Debes cargar al menos una cancha.");
        }

        club.Address = request.Address;
        club.City = request.City;
        club.CourtCount = request.Courts.Count;
        club.FullMatchPrice = request.Courts.FirstOrDefault()?.FullMatchPrice ?? 0m;

        db.ClubSchedules.RemoveRange(club.Schedules);
        foreach (var court in club.Courts)
        {
            db.CourtSchedules.RemoveRange(court.Schedules);
            court.IsActive = false;
        }

        foreach (var courtRequest in request.Courts)
        {
            var court = courtRequest.Id.HasValue
                ? club.Courts.SingleOrDefault(existing => existing.Id == courtRequest.Id.Value)
                : null;

            if (court is null)
            {
                court = new Court();
                club.Courts.Add(court);
            }

            if (string.IsNullOrWhiteSpace(courtRequest.Name))
            {
                return BadRequest("Todas las canchas deben tener nombre.");
            }

            if (courtRequest.Schedules.Count == 0)
            {
                return BadRequest($"La cancha {courtRequest.Name} debe tener al menos un rango horario.");
            }

            court.Name = courtRequest.Name.Trim();
            court.IsActive = courtRequest.IsActive;
            court.IsCovered = courtRequest.IsCovered;
            court.FloorType = courtRequest.FloorType.Trim();
            court.WallType = courtRequest.WallType.Trim();
            court.FullMatchPrice = courtRequest.FullMatchPrice;

            foreach (var schedule in courtRequest.Schedules)
            {
                court.Schedules.Add(new CourtSchedule
                {
                    DayOfWeek = schedule.DayOfWeek,
                    OpensAt = schedule.OpensAt,
                    ClosesAt = schedule.ClosesAt,
                    SlotMinutes = schedule.SlotMinutes
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(club));
    }

    [HttpGet("available-by-start")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyCollection<AvailabilityResponse>>> AvailableByStart(DateTime startsAtUtc, int durationMinutes, CancellationToken cancellationToken)
    {
        return Ok(await availability.GetAvailableByStartAsync(startsAtUtc, durationMinutes, cancellationToken));
    }

    [HttpGet("{clubId:guid}/availability")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyCollection<AvailabilityResponse>>> AvailableByClub(Guid clubId, DateOnly date, CancellationToken cancellationToken)
    {
        return Ok(await availability.GetAvailableByClubAsync(clubId, date, cancellationToken));
    }

    private static ClubResponse ToResponse(Club club)
    {
        var courts = club.Courts
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
            .ToList();

        return new ClubResponse(club.Id, club.Name, club.Status, club.Address, club.City, club.CourtCount, club.FullMatchPrice, club.Owner?.MercadoPagoPublicKey, courts);
    }
}
