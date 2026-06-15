using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Padel.Api.Contracts;
using Padel.Api.Data;
using Padel.Api.Domain;

namespace Padel.Api.Services;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "Padel.Api";
    public string Audience { get; set; } = "Padel.Client";
    public string Key { get; set; } = "dev-only-key-change-me-please-32-chars";
    public int ExpirationMinutes { get; set; } = 120;
}

public sealed class GoogleAuthOptions
{
    public string? ClientId { get; set; }
}

public sealed class MercadoPagoOptions
{
    public string? AccessToken { get; set; }
    public string? SandboxPayerEmail { get; set; }
    public string? SuccessUrl { get; set; }
    public string? FailureUrl { get; set; }
    public string? PendingUrl { get; set; }
    public string? NotificationUrl { get; set; }
}

public interface ISkillMatcher
{
    int GetRank(SkillCategory category, SkillLevel level);
    bool IsCompatible(SkillCategory sourceCategory, SkillLevel sourceLevel, SkillCategory targetCategory, SkillLevel targetLevel);
}

public sealed class SkillMatcher : ISkillMatcher
{
    public int GetRank(SkillCategory category, SkillLevel level) => (((int)category - 1) * 3) + (int)level;

    public bool IsCompatible(SkillCategory sourceCategory, SkillLevel sourceLevel, SkillCategory targetCategory, SkillLevel targetLevel)
    {
        return Math.Abs(GetRank(sourceCategory, sourceLevel) - GetRank(targetCategory, targetLevel)) <= 1;
    }
}

public interface ITokenService
{
    Task<string> CreateTokenAsync(ApplicationUser user);
}

public sealed class TokenService(UserManager<ApplicationUser> userManager, IOptions<JwtOptions> options) : ITokenService
{
    public async Task<string> CreateTokenAsync(ApplicationUser user)
    {
        var jwtOptions = options.Value;
        var roles = await userManager.GetRolesAsync(user);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.FullName),
            new("category", user.Category.ToString()),
            new("level", user.Level.ToString())
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            jwtOptions.Issuer,
            jwtOptions.Audience,
            claims,
            expires: DateTime.UtcNow.AddMinutes(jwtOptions.ExpirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public interface IEmailSender
{
    Task SendConfirmationAsync(ApplicationUser user, string token, CancellationToken cancellationToken);
    Task SendPasswordResetAsync(ApplicationUser user, string token, CancellationToken cancellationToken);
}

public sealed class ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) : IEmailSender
{
    public Task SendConfirmationAsync(ApplicationUser user, string token, CancellationToken cancellationToken)
    {
        logger.LogInformation("Confirmation token for {Email}: {Token}", user.Email, token);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(ApplicationUser user, string token, CancellationToken cancellationToken)
    {
        logger.LogInformation("Password reset token for {Email}: {Token}", user.Email, token);
        return Task.CompletedTask;
    }
}

public interface IGoogleAuthService
{
    Task<GoogleJsonWebSignature.Payload> ValidateAsync(string idToken, CancellationToken cancellationToken);
}

public sealed class GoogleAuthService(IOptions<GoogleAuthOptions> options) : IGoogleAuthService
{
    public Task<GoogleJsonWebSignature.Payload> ValidateAsync(string idToken, CancellationToken cancellationToken)
    {
        var settings = new GoogleJsonWebSignature.ValidationSettings();
        if (!string.IsNullOrWhiteSpace(options.Value.ClientId))
        {
            settings.Audience = [options.Value.ClientId];
        }

        return GoogleJsonWebSignature.ValidateAsync(idToken, settings);
    }
}

public interface INotificationService
{
    Task NotifyEligibleMatchCreatedAsync(PadelMatch match, CancellationToken cancellationToken);
    Task NotifyJoinRequestAsync(JoinRequest request, CancellationToken cancellationToken);
    Task NotifyMatchFullAsync(PadelMatch match, CancellationToken cancellationToken);
    Task NotifyMatchCancelledAsync(PadelMatch match, CancellationToken cancellationToken);
    Task NotifyPlayerLeftAsync(PadelMatch match, string userId, CancellationToken cancellationToken);
    Task NotifyPaymentUpdatedAsync(Payment payment, CancellationToken cancellationToken);
}

public sealed class NotificationService(AppDbContext db, ISkillMatcher matcher) : INotificationService
{
    public async Task NotifyEligibleMatchCreatedAsync(PadelMatch match, CancellationToken cancellationToken)
    {
        var playerIds = await db.UserRoles
            .Join(
                db.Roles.Where(role => role.Name == "Player"),
                userRole => userRole.RoleId,
                role => role.Id,
                (userRole, _) => userRole.UserId)
            .ToListAsync(cancellationToken);

        var users = await db.Users
            .Where(user => user.Id != match.CreatorId && playerIds.Contains(user.Id))
            .ToListAsync(cancellationToken);

        var eligible = users.Where(user => matcher.IsCompatible(match.RequiredCategory, match.RequiredLevel, user.Category, user.Level));
        foreach (var user in eligible)
        {
            db.Notifications.Add(new Notification
            {
                UserId = user.Id,
                Type = NotificationType.MatchCreated,
                Title = "Nuevo turno disponible",
                Message = $"Hay un turno compatible para {match.StartsAtUtc:g}.",
                PayloadJson = JsonSerializer.Serialize(new { match.Id })
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task NotifyJoinRequestAsync(JoinRequest request, CancellationToken cancellationToken)
    {
        var creatorId = await db.Matches
            .Where(match => match.Id == request.MatchId)
            .Select(match => match.CreatorId)
            .SingleAsync(cancellationToken);

        db.Notifications.Add(new Notification
        {
            UserId = creatorId,
            Type = NotificationType.JoinRequestCreated,
            Title = "Solicitud para unirse",
            Message = "Un jugador fuera del rango recomendado quiere sumarse al turno.",
            PayloadJson = JsonSerializer.Serialize(new { request.MatchId, request.Id })
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task NotifyMatchFullAsync(PadelMatch match, CancellationToken cancellationToken)
    {
        var playerIds = await db.MatchPlayers
            .Where(player => player.MatchId == match.Id)
            .Select(player => player.UserId)
            .ToListAsync(cancellationToken);

        foreach (var userId in playerIds)
        {
            db.Notifications.Add(new Notification
            {
                UserId = userId,
                Type = NotificationType.MatchFull,
                Title = "Turno completo",
                Message = "El turno ya tiene los cuatro jugadores confirmados.",
                PayloadJson = JsonSerializer.Serialize(new { match.Id })
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task NotifyMatchCancelledAsync(PadelMatch match, CancellationToken cancellationToken)
    {
        var playerIds = await db.MatchPlayers
            .Where(player => player.MatchId == match.Id)
            .Select(player => player.UserId)
            .ToListAsync(cancellationToken);

        foreach (var userId in playerIds)
        {
            db.Notifications.Add(new Notification
            {
                UserId = userId,
                Type = NotificationType.MatchCancelled,
                Title = "Turno cancelado",
                Message = "El turno fue cancelado y el horario volvió a estar disponible.",
                PayloadJson = JsonSerializer.Serialize(new { match.Id })
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task NotifyPaymentUpdatedAsync(Payment payment, CancellationToken cancellationToken)
    {
        db.Notifications.Add(new Notification
        {
            UserId = payment.UserId,
            Type = NotificationType.PaymentUpdated,
            Title = "Pago actualizado",
            Message = $"El pago del turno quedó en estado {payment.Status}.",
            PayloadJson = JsonSerializer.Serialize(new { payment.MatchId, payment.Id })
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task NotifyPlayerLeftAsync(PadelMatch match, string userId, CancellationToken cancellationToken)
    {
        var playerName = await db.Users
            .Where(user => user.Id == userId)
            .Select(user => user.FullName)
            .SingleOrDefaultAsync(cancellationToken) ?? "Un jugador";

        db.Notifications.Add(new Notification
        {
            UserId = match.CreatorId,
            Type = NotificationType.PlayerLeft,
            Title = "Jugador salió del turno",
            Message = $"{playerName} salio del turno. Su autorizacion de pago fue cancelada.",
            PayloadJson = JsonSerializer.Serialize(new { match.Id, userId })
        });

        await db.SaveChangesAsync(cancellationToken);
    }
}

public interface IAvailabilityService
{
    Task<IReadOnlyCollection<AvailabilityResponse>> GetAvailableByStartAsync(DateTime startsAtUtc, int durationMinutes, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<AvailabilityResponse>> GetAvailableByClubAsync(Guid clubId, DateOnly date, CancellationToken cancellationToken);
    Task<CourtBooking> BlockSlotAsync(Guid courtId, DateTime startsAtUtc, int durationMinutes, CancellationToken cancellationToken);
    Task ReleaseSlotAsync(Guid bookingId, CancellationToken cancellationToken);
}

public sealed class AvailabilityService(AppDbContext db) : IAvailabilityService
{
    public async Task<IReadOnlyCollection<AvailabilityResponse>> GetAvailableByStartAsync(DateTime startsAtUtc, int durationMinutes, CancellationToken cancellationToken)
    {
        var endsAtUtc = startsAtUtc.AddMinutes(durationMinutes);
        var courts = await db.Courts
            .Include(court => court.Schedules)
            .Include(court => court.Club)
            .ThenInclude(club => club.Schedules)
            .Where(court => court.IsActive && court.Club.Status == ClubStatus.Approved)
            .ToListAsync(cancellationToken);

        var available = new List<AvailabilityResponse>();
        foreach (var court in courts.Where(court => IsInsideSchedule(court, startsAtUtc, endsAtUtc)))
        {
            if (!await HasOverlapAsync(court.Id, startsAtUtc, endsAtUtc, cancellationToken))
            {
                available.Add(ToAvailability(court, startsAtUtc, endsAtUtc));
            }
        }

        return available;
    }

    public async Task<IReadOnlyCollection<AvailabilityResponse>> GetAvailableByClubAsync(Guid clubId, DateOnly date, CancellationToken cancellationToken)
    {
        var courts = await db.Courts
            .Include(court => court.Schedules)
            .Include(court => court.Club)
            .ThenInclude(club => club.Schedules)
            .Where(court => court.ClubId == clubId && court.IsActive && court.Club.Status == ClubStatus.Approved)
            .ToListAsync(cancellationToken);

        var result = new List<AvailabilityResponse>();
        foreach (var court in courts)
        {
            foreach (var schedule in GetSchedules(court).Where(schedule => schedule.DayOfWeek == date.DayOfWeek))
            {
                var startsAt = date.ToDateTime(schedule.OpensAt, DateTimeKind.Utc);
                var closesAt = date.ToDateTime(schedule.ClosesAt, DateTimeKind.Utc);
                while (startsAt.AddMinutes(schedule.SlotMinutes) <= closesAt)
                {
                    var endsAt = startsAt.AddMinutes(schedule.SlotMinutes);
                    if (startsAt > DateTime.UtcNow && !await HasOverlapAsync(court.Id, startsAt, endsAt, cancellationToken))
                    {
                        result.Add(ToAvailability(court, startsAt, endsAt));
                    }

                    startsAt = startsAt.AddMinutes(schedule.SlotMinutes);
                }
            }
        }

        return result.OrderBy(slot => slot.StartsAtUtc).ToList();
    }

    public async Task<CourtBooking> BlockSlotAsync(Guid courtId, DateTime startsAtUtc, int durationMinutes, CancellationToken cancellationToken)
    {
        var court = await db.Courts
            .Include(x => x.Schedules)
            .Include(x => x.Club)
            .ThenInclude(x => x.Schedules)
            .SingleOrDefaultAsync(x => x.Id == courtId && x.IsActive && x.Club.Status == ClubStatus.Approved, cancellationToken)
            ?? throw new InvalidOperationException("La cancha no existe o no esta aprobada.");

        var endsAtUtc = startsAtUtc.AddMinutes(durationMinutes);
        if (!IsInsideSchedule(court, startsAtUtc, endsAtUtc))
        {
            throw new InvalidOperationException("El horario no esta dentro de la disponibilidad de la cancha.");
        }

        if (await HasOverlapAsync(courtId, startsAtUtc, endsAtUtc, cancellationToken))
        {
            throw new InvalidOperationException("El horario ya esta bloqueado.");
        }

        var booking = new CourtBooking
        {
            CourtId = courtId,
            StartsAtUtc = startsAtUtc,
            EndsAtUtc = endsAtUtc
        };

        db.CourtBookings.Add(booking);
        await db.SaveChangesAsync(cancellationToken);
        return booking;
    }

    public async Task ReleaseSlotAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var booking = await db.CourtBookings.SingleOrDefaultAsync(x => x.Id == bookingId, cancellationToken);
        if (booking is null)
        {
            return;
        }

        booking.IsCancelled = true;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static AvailabilityResponse ToAvailability(Court court, DateTime startsAtUtc, DateTime endsAtUtc)
    {
        var price = court.FullMatchPrice > 0 ? court.FullMatchPrice : court.Club.FullMatchPrice;
        return new AvailabilityResponse(court.Id, court.Name, court.ClubId, court.Club.Name, startsAtUtc, endsAtUtc, price);
    }

    private static bool IsInsideSchedule(Court court, DateTime startsAtUtc, DateTime endsAtUtc)
    {
        var startTime = TimeOnly.FromDateTime(startsAtUtc);
        var endTime = TimeOnly.FromDateTime(endsAtUtc);

        return GetSchedules(court).Any(schedule =>
            schedule.DayOfWeek == startsAtUtc.DayOfWeek &&
            schedule.OpensAt <= startTime &&
            schedule.ClosesAt >= endTime);
    }

    private static IEnumerable<IScheduleWindow> GetSchedules(Court court)
    {
        if (court.Schedules.Count > 0)
        {
            return court.Schedules.Select(schedule => new ScheduleWindow(schedule.DayOfWeek, schedule.OpensAt, schedule.ClosesAt, schedule.SlotMinutes));
        }

        return court.Club.Schedules.Select(schedule => new ScheduleWindow(schedule.DayOfWeek, schedule.OpensAt, schedule.ClosesAt, schedule.SlotMinutes));
    }

    private interface IScheduleWindow
    {
        DayOfWeek DayOfWeek { get; }
        TimeOnly OpensAt { get; }
        TimeOnly ClosesAt { get; }
        int SlotMinutes { get; }
    }

    private sealed record ScheduleWindow(DayOfWeek DayOfWeek, TimeOnly OpensAt, TimeOnly ClosesAt, int SlotMinutes) : IScheduleWindow;

    private Task<bool> HasOverlapAsync(Guid courtId, DateTime startsAtUtc, DateTime endsAtUtc, CancellationToken cancellationToken)
    {
        return db.CourtBookings.AnyAsync(booking =>
            booking.CourtId == courtId &&
            !booking.IsCancelled &&
            booking.StartsAtUtc < endsAtUtc &&
            startsAtUtc < booking.EndsAtUtc,
            cancellationToken);
    }
}

public interface IMatchService
{
    Task<PadelMatch> CreateAsync(ApplicationUser creator, CreateMatchRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<PadelMatch>> SearchAsync(ApplicationUser user, bool all, CancellationToken cancellationToken);
    Task JoinAsync(Guid matchId, ApplicationUser user, CancellationToken cancellationToken);
    Task LeaveAsync(Guid matchId, string userId, CancellationToken cancellationToken);
    Task<JoinRequest> RequestJoinAsync(Guid matchId, ApplicationUser user, string? message, CancellationToken cancellationToken);
    Task DecideRequestAsync(Guid requestId, string creatorId, bool accept, CancellationToken cancellationToken);
    Task CancelAsync(Guid matchId, string userId, CancellationToken cancellationToken);
    Task<int> CancelIncompleteAsync(DateTime nowUtc, CancellationToken cancellationToken);
}

public sealed class MatchService(
    AppDbContext db,
    ISkillMatcher matcher,
    IAvailabilityService availability,
    INotificationService notifications,
    IMercadoPagoService payments) : IMatchService
{
    public async Task<PadelMatch> CreateAsync(ApplicationUser creator, CreateMatchRequest request, CancellationToken cancellationToken)
    {
        await EnsureUserHasNoActiveMatchAsync(creator.Id, cancellationToken);

        var booking = await availability.BlockSlotAsync(request.CourtId, request.StartsAtUtc, request.DurationMinutes, cancellationToken);
        var match = new PadelMatch
        {
            CreatorId = creator.Id,
            CourtId = request.CourtId,
            CourtBookingId = booking.Id,
            StartsAtUtc = booking.StartsAtUtc,
            EndsAtUtc = booking.EndsAtUtc,
            RequiredCategory = creator.Category,
            RequiredLevel = creator.Level,
            Players =
            {
                new MatchPlayer { UserId = creator.Id, TeamNumber = 1 }
            }
        };

        db.Matches.Add(match);
        await db.SaveChangesAsync(cancellationToken);
        try
        {
            await payments.ReservePlayerPaymentAsync(creator, match.Id, ToPaymentAuthorization(request), cancellationToken);
        }
        catch
        {
            db.Matches.Remove(match);
            await db.SaveChangesAsync(cancellationToken);
            await availability.ReleaseSlotAsync(booking.Id, cancellationToken);
            throw;
        }

        await notifications.NotifyEligibleMatchCreatedAsync(match, cancellationToken);
        return match;
    }

    public async Task<IReadOnlyCollection<PadelMatch>> SearchAsync(ApplicationUser user, bool all, CancellationToken cancellationToken)
    {
        var matches = await QueryMatches()
            .Where(match => match.Status == MatchStatus.Open && match.StartsAtUtc > DateTime.UtcNow && match.Players.Count < 4)
            .Where(match => !match.Players.Any(player => player.UserId == user.Id))
            .OrderBy(match => match.StartsAtUtc)
            .ToListAsync(cancellationToken);

        if (all)
        {
            return matches;
        }

        return matches
            .Where(match => matcher.IsCompatible(match.RequiredCategory, match.RequiredLevel, user.Category, user.Level))
            .ToList();
    }

    public async Task JoinAsync(Guid matchId, ApplicationUser user, CancellationToken cancellationToken)
    {
        var match = await QueryMatches().SingleOrDefaultAsync(x => x.Id == matchId, cancellationToken)
            ?? throw new InvalidOperationException("El turno no existe.");

        EnsureOpen(match);
        if (!matcher.IsCompatible(match.RequiredCategory, match.RequiredLevel, user.Category, user.Level))
        {
            throw new InvalidOperationException("El jugador esta fuera de rango y debe enviar una solicitud.");
        }

        await AddPlayerAsync(match, user.Id, cancellationToken);
    }

    public async Task LeaveAsync(Guid matchId, string userId, CancellationToken cancellationToken)
    {
        var match = await QueryMatches().SingleOrDefaultAsync(x => x.Id == matchId, cancellationToken)
            ?? throw new InvalidOperationException("El turno no existe.");

        if (match.CreatorId == userId)
        {
            throw new InvalidOperationException("El creador debe cancelar el turno completo.");
        }

        if (match.Status is MatchStatus.Cancelled or MatchStatus.Completed)
        {
            throw new InvalidOperationException("No puedes salir de un turno cancelado o finalizado.");
        }

        if (match.StartsAtUtc <= DateTime.UtcNow)
        {
            throw new InvalidOperationException("No puedes salir de un turno que ya comenzo.");
        }

        var player = match.Players.SingleOrDefault(player => player.UserId == userId)
            ?? throw new InvalidOperationException("No estas dentro de este turno.");

        var userPayments = match.Payments
            .Where(payment => payment.UserId == userId && payment.Status is PaymentStatus.Pending or PaymentStatus.Authorized)
            .ToList();
        foreach (var payment in userPayments)
        {
            await payments.CancelAuthorizedPaymentAsync(payment.Id, cancellationToken);
        }

        match.Players.Remove(player);
        if (match.Status == MatchStatus.Full)
        {
            match.Status = MatchStatus.Open;
        }

        await db.SaveChangesAsync(cancellationToken);
        await notifications.NotifyPlayerLeftAsync(match, userId, cancellationToken);
    }

    public async Task<JoinRequest> RequestJoinAsync(Guid matchId, ApplicationUser user, string? message, CancellationToken cancellationToken)
    {
        var match = await QueryMatches().SingleOrDefaultAsync(x => x.Id == matchId, cancellationToken)
            ?? throw new InvalidOperationException("El turno no existe.");

        EnsureOpen(match);
        if (match.Players.Any(player => player.UserId == user.Id))
        {
            throw new InvalidOperationException("El jugador ya esta en el turno.");
        }

        var existing = match.JoinRequests.SingleOrDefault(request => request.UserId == user.Id);
        if (existing is { Status: JoinRequestStatus.Pending })
        {
            return existing;
        }

        var request = new JoinRequest
        {
            MatchId = matchId,
            UserId = user.Id,
            Message = message
        };

        db.JoinRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);
        await notifications.NotifyJoinRequestAsync(request, cancellationToken);
        return request;
    }

    public async Task DecideRequestAsync(Guid requestId, string creatorId, bool accept, CancellationToken cancellationToken)
    {
        var request = await db.JoinRequests
            .Include(x => x.Match)
            .ThenInclude(x => x.Players)
            .SingleOrDefaultAsync(x => x.Id == requestId, cancellationToken)
            ?? throw new InvalidOperationException("La solicitud no existe.");

        if (request.Match.CreatorId != creatorId)
        {
            throw new InvalidOperationException("Solo el creador puede resolver solicitudes.");
        }

        if (request.Status != JoinRequestStatus.Pending)
        {
            return;
        }

        request.Status = accept ? JoinRequestStatus.Accepted : JoinRequestStatus.Rejected;
        if (accept)
        {
            await AddPlayerAsync(request.Match, request.UserId, cancellationToken);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task CancelAsync(Guid matchId, string userId, CancellationToken cancellationToken)
    {
        var match = await QueryMatches().SingleOrDefaultAsync(x => x.Id == matchId, cancellationToken)
            ?? throw new InvalidOperationException("El turno no existe.");

        if (match.CreatorId != userId)
        {
            throw new InvalidOperationException("Solo el creador puede cancelar el turno.");
        }

        await CancelMatchAsync(match, cancellationToken);
    }

    public async Task<int> CancelIncompleteAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var cutoff = nowUtc.AddHours(2);
        var matches = await QueryMatches()
            .Where(match => match.Status == MatchStatus.Open && match.StartsAtUtc <= cutoff && match.Players.Count < 4)
            .ToListAsync(cancellationToken);

        foreach (var match in matches)
        {
            await CancelMatchAsync(match, cancellationToken);
        }

        return matches.Count;
    }

    private IQueryable<PadelMatch> QueryMatches()
    {
        return db.Matches
            .Include(match => match.Court)
            .ThenInclude(court => court.Club)
            .Include(match => match.Players)
            .ThenInclude(player => player.User)
            .Include(match => match.JoinRequests)
            .Include(match => match.Payments);
    }

    private async Task AddPlayerAsync(PadelMatch match, string userId, CancellationToken cancellationToken)
    {
        EnsureOpen(match);
        if (match.Players.Any(player => player.UserId == userId))
        {
            throw new InvalidOperationException("El jugador ya esta en el turno.");
        }

        if (match.Players.Count >= 4)
        {
            throw new InvalidOperationException("El turno ya esta completo.");
        }

        await EnsureUserHasNoActiveMatchAsync(userId, cancellationToken, match.Id);

        var team = match.Players.Count(player => player.TeamNumber == 1) <= match.Players.Count(player => player.TeamNumber == 2) ? 1 : 2;
        match.Players.Add(new MatchPlayer { MatchId = match.Id, UserId = userId, TeamNumber = team });

        var wasFull = match.Status == MatchStatus.Full;
        if (match.Players.Count == 4)
        {
            match.Status = MatchStatus.Full;
        }

        await db.SaveChangesAsync(cancellationToken);
        try
        {
            var user = await db.Users.SingleAsync(x => x.Id == userId, cancellationToken);
            await payments.ReservePlayerPaymentAsync(user, match.Id, null, cancellationToken);
        }
        catch
        {
            var addedPlayer = match.Players.SingleOrDefault(player => player.UserId == userId);
            if (addedPlayer is not null)
            {
                match.Players.Remove(addedPlayer);
            }

            if (!wasFull && match.Status == MatchStatus.Full)
            {
                match.Status = MatchStatus.Open;
            }

            await db.SaveChangesAsync(cancellationToken);
            throw;
        }

        if (match.Status == MatchStatus.Full)
        {
            await notifications.NotifyMatchFullAsync(match, cancellationToken);
        }
    }

    private async Task CancelMatchAsync(PadelMatch match, CancellationToken cancellationToken)
    {
        if (match.Status == MatchStatus.Cancelled)
        {
            return;
        }

        var activePaymentIds = match.Payments
            .Where(payment => payment.Status is PaymentStatus.Pending or PaymentStatus.Authorized)
            .Select(payment => payment.Id)
            .ToList();
        foreach (var paymentId in activePaymentIds)
        {
            await payments.CancelAuthorizedPaymentAsync(paymentId, cancellationToken);
        }

        match.Status = MatchStatus.Cancelled;
        await db.SaveChangesAsync(cancellationToken);
        await availability.ReleaseSlotAsync(match.CourtBookingId, cancellationToken);
        await notifications.NotifyMatchCancelledAsync(match, cancellationToken);
    }

    private static void EnsureOpen(PadelMatch match)
    {
        if (match.Status != MatchStatus.Open)
        {
            throw new InvalidOperationException("El turno no esta abierto.");
        }
    }

    private static PaymentAuthorizationRequest? ToPaymentAuthorization(CreateMatchRequest request)
    {
        return string.IsNullOrWhiteSpace(request.CardToken) || string.IsNullOrWhiteSpace(request.PaymentMethodId)
            ? null
            : new PaymentAuthorizationRequest(
                request.CardToken,
                request.PaymentMethodId,
                request.CardBrand,
                request.LastFourDigits);
    }

    private async Task EnsureUserHasNoActiveMatchAsync(
        string userId,
        CancellationToken cancellationToken,
        Guid? excludeMatchId = null)
    {
        var nowUtc = DateTime.UtcNow;
        var hasActiveMatch = await db.MatchPlayers.AnyAsync(player =>
            player.UserId == userId &&
            (!excludeMatchId.HasValue || player.MatchId != excludeMatchId.Value) &&
            player.Match.Status != MatchStatus.Cancelled &&
            player.Match.Status != MatchStatus.Completed &&
            player.Match.EndsAtUtc > nowUtc,
            cancellationToken);

        if (hasActiveMatch)
        {
            throw new InvalidOperationException("Ya tienes un turno activo. No puedes crear o unirte a otro hasta que termine o se cancele.");
        }
    }
}

public interface IMercadoPagoService
{
    Task<PaymentPreferenceResponse> CreatePreferenceAsync(ApplicationUser user, Guid matchId, CancellationToken cancellationToken);
    Task<PaymentPreferenceResponse> ReservePlayerPaymentAsync(ApplicationUser user, Guid matchId, PaymentAuthorizationRequest? authorization, CancellationToken cancellationToken);
    Task UpdatePaymentAsync(string providerPaymentId, PaymentStatus status, CancellationToken cancellationToken);
    Task SyncPaymentFromProviderAsync(Guid paymentId, string providerPaymentId, CancellationToken cancellationToken);
    Task CancelAuthorizedPaymentAsync(Guid paymentId, CancellationToken cancellationToken);
    Task<int> CaptureFinishedMatchPaymentsAsync(DateTime nowUtc, CancellationToken cancellationToken);
}

public sealed record PaymentAuthorizationRequest(
    string CardToken,
    string PaymentMethodId,
    string? CardBrand,
    string? LastFourDigits);

public sealed class MercadoPagoService(
    AppDbContext db,
    HttpClient httpClient,
    IOptions<MercadoPagoOptions> options,
    INotificationService notifications) : IMercadoPagoService
{
    private const string MercadoPagoAccountMethod = "mercadopago_account";
    private const decimal OwnerSharePercent = 0.93m;
    private const decimal AdminFeePercent = 0.03m;
    private const decimal ProcessingReservePercent = 0.04m;
    private static readonly TimeSpan AuthorizationWindow = TimeSpan.FromDays(7);

    public async Task<PaymentPreferenceResponse> CreatePreferenceAsync(ApplicationUser user, Guid matchId, CancellationToken cancellationToken)
    {
        var match = await db.Matches
            .Include(x => x.Court)
            .ThenInclude(x => x.Club)
            .ThenInclude(x => x.Owner)
            .Include(x => x.Players)
            .Include(x => x.Payments)
            .SingleOrDefaultAsync(x => x.Id == matchId, cancellationToken)
            ?? throw new InvalidOperationException("El turno no existe.");

        if (!match.Players.Any(player => player.UserId == user.Id))
        {
            throw new InvalidOperationException("Solo los jugadores del turno pueden autorizar su pago.");
        }

        if (match.Status is MatchStatus.Cancelled or MatchStatus.Completed)
        {
            throw new InvalidOperationException("El turno no permite nuevos pagos.");
        }

        if (match.StartsAtUtc > DateTime.UtcNow.Add(AuthorizationWindow))
        {
            throw new InvalidOperationException("Mercado Pago permite reservar pagos por un tiempo limitado. Autoriza el pago cuando falten menos de 7 dias para el turno.");
        }

        var existingPayment = match.Payments
            .Where(payment => payment.UserId == user.Id && payment.Status is PaymentStatus.Pending or PaymentStatus.Authorized)
            .OrderByDescending(payment => payment.CreatedAtUtc)
            .FirstOrDefault();
        if (existingPayment is not null && !string.IsNullOrWhiteSpace(existingPayment.CheckoutUrl))
        {
            return ToPreferenceResponse(existingPayment);
        }

        var fullMatchPrice = match.Court.FullMatchPrice > 0 ? match.Court.FullMatchPrice : match.Court.Club.FullMatchPrice;
        var playerAmount = RoundMoney(fullMatchPrice / 4m);
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            UserId = user.Id,
            Amount = playerAmount,
            OwnerAmount = RoundMoney(playerAmount * OwnerSharePercent),
            AdminFeeAmount = RoundMoney(playerAmount * AdminFeePercent),
            ProcessingReserveAmount = RoundMoney(playerAmount * ProcessingReservePercent)
        };

        var settings = await db.MercadoPagoSettings.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        var ownerAccessToken = match.Court.Club.Owner.MercadoPagoAccessToken;
        if (string.IsNullOrWhiteSpace(ownerAccessToken))
        {
            throw new InvalidOperationException("El dueño del club debe configurar su access token de Mercado Pago para recibir el 93% del pago.");
        }

        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerAccessToken);
        var payload = new Dictionary<string, object?>
        {
            ["items"] = new[]
            {
                new
                {
                    title = $"Turno de padel {match.StartsAtUtc:g}",
                    quantity = 1,
                    unit_price = payment.Amount,
                    currency_id = "ARS"
                }
            },
            ["capture"] = false,
            ["marketplace_fee"] = payment.AdminFeeAmount,
            ["back_urls"] = new
            {
                success = FirstNotEmpty(settings?.SuccessUrl, options.Value.SuccessUrl),
                failure = FirstNotEmpty(settings?.FailureUrl, options.Value.FailureUrl),
                pending = FirstNotEmpty(settings?.PendingUrl, options.Value.PendingUrl)
            }
        };
        var notificationUrl = BuildNotificationUrl(FirstNotEmpty(settings?.NotificationUrl, options.Value.NotificationUrl), payment.Id);
        if (notificationUrl is not null)
        {
            payload["notification_url"] = notificationUrl;
        }

        var response = await httpClient.PostAsJsonAsync("https://api.mercadopago.com/checkout/preferences", payload, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Mercado Pago rechazo la preferencia ({(int)response.StatusCode}). {responseBody}");
        }

        using var document = JsonDocument.Parse(responseBody);
        var preferenceId = document.RootElement.GetProperty("id").GetString() ?? $"local-{Guid.NewGuid():N}";
        var checkoutUrl = GetCheckoutUrl(document.RootElement, settings?.Environment ?? MercadoPagoEnvironment.Sandbox)
            ?? throw new InvalidOperationException("Mercado Pago no devolvio una URL de checkout valida.");

        payment.ProviderPreferenceId = preferenceId;
        payment.CheckoutUrl = checkoutUrl;
        payment.AuthorizationExpiresAtUtc = payment.CreatedAtUtc.Add(AuthorizationWindow);
        db.Payments.Add(payment);
        await db.SaveChangesAsync(cancellationToken);

        return ToPreferenceResponse(payment);
    }

    public async Task<PaymentPreferenceResponse> ReservePlayerPaymentAsync(ApplicationUser user, Guid matchId, PaymentAuthorizationRequest? authorization, CancellationToken cancellationToken)
    {
        var match = await db.Matches
            .Include(x => x.Court)
            .ThenInclude(x => x.Club)
            .ThenInclude(x => x.Owner)
            .Include(x => x.Players)
            .Include(x => x.Payments)
            .SingleOrDefaultAsync(x => x.Id == matchId, cancellationToken)
            ?? throw new InvalidOperationException("El turno no existe.");

        if (!match.Players.Any(player => player.UserId == user.Id))
        {
            throw new InvalidOperationException("Solo los jugadores del turno pueden reservar su pago.");
        }

        if (match.StartsAtUtc > DateTime.UtcNow.Add(AuthorizationWindow))
        {
            throw new InvalidOperationException("Solo puedes reservar el pago cuando falten menos de 7 dias para el turno.");
        }

        var existingPayment = match.Payments
            .Where(payment => payment.UserId == user.Id && payment.Status is PaymentStatus.Pending or PaymentStatus.Authorized)
            .OrderByDescending(payment => payment.CreatedAtUtc)
            .FirstOrDefault();
        if (existingPayment is not null)
        {
            return ToPreferenceResponse(existingPayment);
        }

        var method = authorization is null
            ? await db.PlayerPaymentMethods.SingleOrDefaultAsync(x => x.UserId == user.Id && x.IsActive, cancellationToken)
            : new PlayerPaymentMethod
            {
                UserId = user.Id,
                CardToken = authorization.CardToken,
                PaymentMethodId = authorization.PaymentMethodId,
                CardBrand = authorization.CardBrand,
                LastFourDigits = authorization.LastFourDigits,
                IsActive = true
            };
        if (method is null)
        {
            throw new InvalidOperationException("Configura un medio de pago en la seccion Pagos antes de crear o unirte a un turno.");
        }

        if (method.PaymentMethodId == MercadoPagoAccountMethod)
        {
            throw new InvalidOperationException("Agrega una tarjeta en la seccion Pagos antes de crear o unirte a un turno.");
        }

        if (string.IsNullOrWhiteSpace(method.CardToken) &&
            string.IsNullOrWhiteSpace(method.MercadoPagoCardId))
        {
            throw new InvalidOperationException("Agrega una tarjeta en la seccion Pagos antes de crear o unirte a un turno.");
        }

        var settings = await db.MercadoPagoSettings.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        var environment = settings?.Environment ?? MercadoPagoEnvironment.Sandbox;
        var ownerAccessToken = match.Court.Club.Owner.MercadoPagoAccessToken;
        if (string.IsNullOrWhiteSpace(ownerAccessToken))
        {
            throw new InvalidOperationException("El dueño del club debe vincular Mercado Pago para poder cobrar turnos.");
        }

        var fullMatchPrice = match.Court.FullMatchPrice > 0 ? match.Court.FullMatchPrice : match.Court.Club.FullMatchPrice;
        var playerAmount = RoundMoney(fullMatchPrice / 4m);
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            UserId = user.Id,
            Amount = playerAmount,
            OwnerAmount = RoundMoney(playerAmount * OwnerSharePercent),
            AdminFeeAmount = RoundMoney(playerAmount * AdminFeePercent),
            ProcessingReserveAmount = RoundMoney(playerAmount * ProcessingReservePercent),
            AuthorizationExpiresAtUtc = DateTime.UtcNow.Add(AuthorizationWindow)
        };

        var payer = new Dictionary<string, object?>
        {
            ["email"] = GetPaymentPayerEmail(user, environment)
        };
        if (!string.IsNullOrWhiteSpace(method.MercadoPagoCustomerId))
        {
            payer["id"] = method.MercadoPagoCustomerId;
        }

        var payload = new Dictionary<string, object?>
        {
            ["transaction_amount"] = payment.Amount,
            ["description"] = $"Turno de padel {match.StartsAtUtc:g}",
            ["installments"] = 1,
            ["payment_method_id"] = method.PaymentMethodId,
            ["capture"] = false,
            ["application_fee"] = payment.AdminFeeAmount,
            ["external_reference"] = payment.Id.ToString(),
            ["payer"] = payer
        };

        if (!string.IsNullOrWhiteSpace(method.CardToken))
        {
            payload["token"] = method.CardToken;
        }
        else
        {
            payload["card_id"] = method.MercadoPagoCardId;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.mercadopago.com/v1/payments")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerAccessToken);
        request.Headers.Add("X-Idempotency-Key", payment.Id.ToString());

        var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (environment == MercadoPagoEnvironment.Sandbox &&
                responseBody.Contains("Invalid test user email", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Mercado Pago Sandbox solo acepta emails de compradores de prueba. Configura MercadoPago:SandboxPayerEmail con el email de un test user comprador de Mercado Pago.");
            }

            throw new InvalidOperationException($"Mercado Pago rechazo la reserva del pago ({(int)response.StatusCode}). {responseBody}");
        }

        using var document = JsonDocument.Parse(responseBody);
        var providerPaymentId = document.RootElement.TryGetProperty("id", out var idElement)
            ? idElement.ToString()
            : string.Empty;
        var providerStatus = document.RootElement.TryGetProperty("status", out var statusElement)
            ? statusElement.GetString()
            : null;
        var mappedStatus = MapProviderStatus(providerStatus);
        if (mappedStatus is PaymentStatus.Rejected or PaymentStatus.Cancelled)
        {
            throw new InvalidOperationException("Mercado Pago rechazo la reserva. Verifica fondos disponibles o cambia tu medio de pago.");
        }

        payment.ProviderAuthorizedPaymentId = providerPaymentId;
        payment.ProviderPaymentId = providerPaymentId;
        payment.Status = mappedStatus == PaymentStatus.Pending ? PaymentStatus.Pending : PaymentStatus.Authorized;
        if (payment.Status == PaymentStatus.Authorized)
        {
            payment.AuthorizedAtUtc = DateTime.UtcNow;
        }

        db.Payments.Add(payment);
        await db.SaveChangesAsync(cancellationToken);
        await notifications.NotifyPaymentUpdatedAsync(payment, cancellationToken);
        return ToPreferenceResponse(payment);
    }

    public async Task UpdatePaymentAsync(string providerPaymentId, PaymentStatus status, CancellationToken cancellationToken)
    {
        var payment = await db.Payments.SingleOrDefaultAsync(x => x.ProviderPaymentId == providerPaymentId || x.ProviderPreferenceId == providerPaymentId, cancellationToken)
            ?? throw new InvalidOperationException("El pago no existe.");

        payment.ProviderPaymentId = providerPaymentId;
        payment.Status = status;
        if (status == PaymentStatus.Authorized)
        {
            payment.ProviderAuthorizedPaymentId = providerPaymentId;
            payment.AuthorizedAtUtc = DateTime.UtcNow;
        }

        if (status == PaymentStatus.Captured || status == PaymentStatus.Approved)
        {
            payment.CapturedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        await notifications.NotifyPaymentUpdatedAsync(payment, cancellationToken);
    }

    public async Task SyncPaymentFromProviderAsync(Guid paymentId, string providerPaymentId, CancellationToken cancellationToken)
    {
        var payment = await db.Payments
            .Include(x => x.Match)
            .ThenInclude(x => x.Court)
            .ThenInclude(x => x.Club)
            .ThenInclude(x => x.Owner)
            .SingleOrDefaultAsync(x => x.Id == paymentId, cancellationToken)
            ?? throw new InvalidOperationException("El pago no existe.");

        var accessToken = payment.Match.Court.Club.Owner.MercadoPagoAccessToken;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("El dueño del club no tiene Mercado Pago configurado.");
        }

        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await httpClient.GetAsync($"https://api.mercadopago.com/v1/payments/{providerPaymentId}", cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"No se pudo consultar el pago en Mercado Pago ({(int)response.StatusCode}). {responseBody}");
        }

        using var document = JsonDocument.Parse(responseBody);
        payment.ProviderPaymentId = providerPaymentId;
        if (document.RootElement.TryGetProperty("status", out var statusElement))
        {
            payment.Status = MapProviderStatus(statusElement.GetString());
            if (payment.Status == PaymentStatus.Authorized)
            {
                payment.ProviderAuthorizedPaymentId = providerPaymentId;
                payment.AuthorizedAtUtc ??= DateTime.UtcNow;
            }

            if (payment.Status is PaymentStatus.Approved or PaymentStatus.Captured)
            {
                payment.CapturedAtUtc ??= DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await notifications.NotifyPaymentUpdatedAsync(payment, cancellationToken);
    }

    public async Task CancelAuthorizedPaymentAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var payment = await db.Payments
            .Include(x => x.Match)
            .ThenInclude(x => x.Court)
            .ThenInclude(x => x.Club)
            .ThenInclude(x => x.Owner)
            .SingleOrDefaultAsync(x => x.Id == paymentId, cancellationToken)
            ?? throw new InvalidOperationException("El pago no existe.");

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.Approved)
        {
            throw new InvalidOperationException("No se puede salir del turno porque el pago ya fue capturado.");
        }

        if (payment.Status == PaymentStatus.Authorized && !string.IsNullOrWhiteSpace(payment.ProviderAuthorizedPaymentId))
        {
            var accessToken = payment.Match.Court.Club.Owner.MercadoPagoAccessToken;
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new InvalidOperationException("El dueño del club no tiene Mercado Pago configurado.");
            }

            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var response = await httpClient.PutAsJsonAsync(
                $"https://api.mercadopago.com/v1/payments/{payment.ProviderAuthorizedPaymentId}",
                new { status = "cancelled" },
                cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Mercado Pago rechazo la cancelacion de la autorizacion ({(int)response.StatusCode}). {responseBody}");
            }
        }

        payment.Status = PaymentStatus.Cancelled;
        await db.SaveChangesAsync(cancellationToken);
        await notifications.NotifyPaymentUpdatedAsync(payment, cancellationToken);
    }

    public async Task<int> CaptureFinishedMatchPaymentsAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var matches = await db.Matches
            .Include(match => match.Court)
            .ThenInclude(court => court.Club)
            .ThenInclude(club => club.Owner)
            .Include(match => match.Payments)
            .Where(match => match.Status == MatchStatus.Full && match.EndsAtUtc <= nowUtc)
            .ToListAsync(cancellationToken);

        var captured = 0;
        foreach (var match in matches)
        {
            var authorizations = match.Payments
                .Where(payment => payment.Status == PaymentStatus.Authorized && !string.IsNullOrWhiteSpace(payment.ProviderAuthorizedPaymentId))
                .ToList();
            foreach (var payment in authorizations)
            {
                var accessToken = match.Court.Club.Owner.MercadoPagoAccessToken;
                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    throw new InvalidOperationException("El dueño del club no tiene Mercado Pago configurado.");
                }

                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                var response = await httpClient.PutAsJsonAsync(
                    $"https://api.mercadopago.com/v1/payments/{payment.ProviderAuthorizedPaymentId}",
                    new { capture = true },
                    cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"Mercado Pago rechazo la captura ({(int)response.StatusCode}). {responseBody}");
                }

                payment.Status = PaymentStatus.Captured;
                payment.ProviderPaymentId = payment.ProviderAuthorizedPaymentId;
                payment.CapturedAtUtc = DateTime.UtcNow;
                captured++;
                await notifications.NotifyPaymentUpdatedAsync(payment, cancellationToken);
            }

            match.Status = MatchStatus.Completed;
        }

        await db.SaveChangesAsync(cancellationToken);
        return captured;
    }

    private string? GetPaymentPayerEmail(ApplicationUser user, MercadoPagoEnvironment environment)
    {
        if (environment == MercadoPagoEnvironment.Sandbox &&
            !string.IsNullOrWhiteSpace(options.Value.SandboxPayerEmail))
        {
            return options.Value.SandboxPayerEmail;
        }

        return user.Email;
    }

    private static string? FirstNotEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static decimal RoundMoney(decimal amount)
    {
        return Math.Round(amount, 2, MidpointRounding.AwayFromZero);
    }

    private static string? BuildNotificationUrl(string? baseUrl, Guid paymentId)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        var separator = baseUrl.Contains('?') ? "&" : "?";
        return $"{baseUrl}{separator}paymentId={paymentId}";
    }

    private static PaymentStatus MapProviderStatus(string? status)
    {
        return status switch
        {
            "authorized" => PaymentStatus.Authorized,
            "approved" => PaymentStatus.Captured,
            "rejected" or "cancelled" => PaymentStatus.Rejected,
            "refunded" => PaymentStatus.Refunded,
            _ => PaymentStatus.Pending
        };
    }

    private static PaymentPreferenceResponse ToPreferenceResponse(Payment payment)
    {
        return new PaymentPreferenceResponse(
            payment.Id,
            payment.ProviderPreferenceId ?? payment.ProviderAuthorizedPaymentId ?? string.Empty,
            payment.CheckoutUrl ?? string.Empty,
            payment.Status,
            payment.Amount,
            payment.OwnerAmount,
            payment.AdminFeeAmount,
            payment.ProcessingReserveAmount);
    }

    private static string? GetCheckoutUrl(JsonElement root, MercadoPagoEnvironment environment)
    {
        if (environment == MercadoPagoEnvironment.Sandbox &&
            root.TryGetProperty("sandbox_init_point", out var sandboxInitPoint) &&
            !string.IsNullOrWhiteSpace(sandboxInitPoint.GetString()))
        {
            return sandboxInitPoint.GetString();
        }

        return root.TryGetProperty("init_point", out var initPoint)
            ? initPoint.GetString()
            : null;
    }
}

public sealed class MatchCancellationWorker(IServiceProvider serviceProvider, ILogger<MatchCancellationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var matches = scope.ServiceProvider.GetRequiredService<IMatchService>();
                var payments = scope.ServiceProvider.GetRequiredService<IMercadoPagoService>();
                var cancelled = await matches.CancelIncompleteAsync(DateTime.UtcNow, stoppingToken);
                if (cancelled > 0)
                {
                    logger.LogInformation("Cancelled {Count} incomplete matches.", cancelled);
                }

                var captured = await payments.CaptureFinishedMatchPaymentsAsync(DateTime.UtcNow, stoppingToken);
                if (captured > 0)
                {
                    logger.LogInformation("Captured {Count} finished match payments.", captured);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error while cancelling incomplete matches.");
            }
        }
    }
}
