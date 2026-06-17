using Microsoft.AspNetCore.Identity;

namespace Padel.Api.Domain;

public sealed class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? Phone { get; set; }
    public string? Bio { get; set; }
    public string? ProfilePhotoUrl { get; set; }
    public string? MercadoPagoAccountEmail { get; set; }
    public string? MercadoPagoAccessToken { get; set; }
    public string? MercadoPagoRefreshToken { get; set; }
    public string? MercadoPagoPublicKey { get; set; }
    public string? MercadoPagoUserId { get; set; }
    public DateTime? MercadoPagoTokenExpiresAtUtc { get; set; }
    public DateTime? MercadoPagoLinkedAtUtc { get; set; }
    public SkillCategory Category { get; set; } = SkillCategory.Octava;
    public SkillLevel Level { get; set; } = SkillLevel.Bajo;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class UserFollow
{
    public string FollowerId { get; set; } = string.Empty;
    public ApplicationUser Follower { get; set; } = null!;
    public string FollowedId { get; set; } = string.Empty;
    public ApplicationUser Followed { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Club
{
    public Guid Id { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public ApplicationUser Owner { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public ClubStatus Status { get; set; } = ClubStatus.PendingApproval;
    public string? Address { get; set; }
    public string? City { get; set; }
    public decimal FullMatchPrice { get; set; }
    public int CourtCount { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<Court> Courts { get; set; } = new List<Court>();
    public ICollection<ClubSchedule> Schedules { get; set; } = new List<ClubSchedule>();
}

public sealed class Court
{
    public Guid Id { get; set; }
    public Guid ClubId { get; set; }
    public Club Club { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public bool IsCovered { get; set; }
    public string FloorType { get; set; } = string.Empty;
    public string WallType { get; set; } = string.Empty;
    public decimal FullMatchPrice { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<CourtSchedule> Schedules { get; set; } = new List<CourtSchedule>();
}

public sealed class ClubSchedule
{
    public Guid Id { get; set; }
    public Guid ClubId { get; set; }
    public Club Club { get; set; } = null!;
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly OpensAt { get; set; }
    public TimeOnly ClosesAt { get; set; }
    public int SlotMinutes { get; set; } = 90;
}

public sealed class CourtSchedule
{
    public Guid Id { get; set; }
    public Guid CourtId { get; set; }
    public Court Court { get; set; } = null!;
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly OpensAt { get; set; }
    public TimeOnly ClosesAt { get; set; }
    public int SlotMinutes { get; set; } = 90;
}

public sealed class CourtBooking
{
    public Guid Id { get; set; }
    public Guid CourtId { get; set; }
    public Court Court { get; set; } = null!;
    public PadelMatch? Match { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public bool IsCancelled { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class PadelMatch
{
    public Guid Id { get; set; }
    public string CreatorId { get; set; } = string.Empty;
    public ApplicationUser Creator { get; set; } = null!;
    public Guid CourtId { get; set; }
    public Court Court { get; set; } = null!;
    public Guid CourtBookingId { get; set; }
    public CourtBooking CourtBooking { get; set; } = null!;
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public SkillCategory RequiredCategory { get; set; }
    public SkillLevel RequiredLevel { get; set; }
    public MatchStatus Status { get; set; } = MatchStatus.Open;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<MatchPlayer> Players { get; set; } = new List<MatchPlayer>();
    public ICollection<JoinRequest> JoinRequests { get; set; } = new List<JoinRequest>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

public sealed class MatchPlayer
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public PadelMatch Match { get; set; } = null!;
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;
    public int TeamNumber { get; set; }
    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class JoinRequest
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public PadelMatch Match { get; set; } = null!;
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;
    public JoinRequestStatus Status { get; set; } = JoinRequestStatus.Pending;
    public string? Message { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Payment
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public PadelMatch Match { get; set; } = null!;
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;
    public decimal Amount { get; set; }
    public decimal OwnerAmount { get; set; }
    public decimal AdminFeeAmount { get; set; }
    public decimal ProcessingReserveAmount { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string? ProviderPreferenceId { get; set; }
    public string? ProviderAuthorizedPaymentId { get; set; }
    public string? ProviderPaymentId { get; set; }
    public string? CheckoutUrl { get; set; }
    public DateTime? AuthorizedAtUtc { get; set; }
    public DateTime? AuthorizationExpiresAtUtc { get; set; }
    public DateTime? CapturedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class PlayerPaymentMethod
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;
    public string? MercadoPagoCustomerId { get; set; }
    public string? MercadoPagoAccountEmail { get; set; }
    public string? MercadoPagoCardId { get; set; }
    public string? CardToken { get; set; }
    public string PaymentMethodId { get; set; } = string.Empty;
    public string? CardBrand { get; set; }
    public string? LastFourDigits { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime LinkedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class MercadoPagoSettings
{
    public int Id { get; set; } = 1;
    public MercadoPagoEnvironment Environment { get; set; } = MercadoPagoEnvironment.Sandbox;
    public string? AccessToken { get; set; }
    public string? PublicKey { get; set; }
    public string? OAuthClientId { get; set; }
    public string? OAuthClientSecret { get; set; }
    public string? OAuthRedirectUrl { get; set; }
    public string? SuccessUrl { get; set; }
    public string? FailureUrl { get; set; }
    public string? PendingUrl { get; set; }
    public string? NotificationUrl { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class MercadoPagoOAuthState
{
    public string State { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;
    public string Purpose { get; set; } = "ClubOwner";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UsedAtUtc { get; set; }
}

public sealed class Notification
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;
    public NotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
