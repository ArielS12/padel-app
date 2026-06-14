using Padel.Api.Domain;

namespace Padel.Api.Contracts;

public sealed record AuthResponse(string Token, UserSummary User);

public sealed record RegisterRequest(
    string Email,
    string Password,
    string FullName,
    SkillCategory Category,
    SkillLevel Level,
    string? City,
    string? Phone);

public sealed record RegisterClubOwnerRequest(
    string Email,
    string Password,
    string FullName,
    string? Phone);

public sealed record LoginRequest(string Email, string Password);

public sealed record GoogleLoginRequest(string IdToken, SkillCategory? Category, SkillLevel? Level);

public sealed record ConfirmEmailRequest(string UserId, string Token);

public sealed record ForgotPasswordRequest(string Email);

public sealed record OwnerAccountResponse(
    string Email,
    string FullName,
    string? Phone,
    string? MercadoPagoAccountEmail,
    string? MercadoPagoPublicKey,
    string? MercadoPagoUserId,
    DateTime? MercadoPagoLinkedAtUtc,
    bool HasMercadoPagoAccessToken);

public sealed record MercadoPagoConnectResponse(string AuthorizationUrl);

public sealed record UpdateOwnerAccountRequest(
    string FullName,
    string? Phone,
    string? CurrentPassword,
    string? NewPassword,
    string? MercadoPagoAccountEmail,
    string? MercadoPagoAccessToken,
    string? MercadoPagoPublicKey);

public sealed record UserSummary(
    string Id,
    string Email,
    string FullName,
    SkillCategory Category,
    SkillLevel Level,
    string? ProfilePhotoUrl);

public sealed record ProfileResponse(
    string Id,
    string Email,
    string FullName,
    string? City,
    string? Phone,
    string? Bio,
    string? ProfilePhotoUrl,
    SkillCategory Category,
    SkillLevel Level,
    int Followers,
    int Following);

public sealed record UpdateProfileRequest(
    string FullName,
    string? City,
    string? Phone,
    string? Bio,
    string? ProfilePhotoUrl,
    SkillCategory Category,
    SkillLevel Level);

public sealed record CreateClubRequest(string Name);

public sealed record CompleteClubRequest(
    string Address,
    string City,
    IReadOnlyCollection<CourtUpsertRequest> Courts);

public sealed record ClubScheduleRequest(DayOfWeek DayOfWeek, TimeOnly OpensAt, TimeOnly ClosesAt, int SlotMinutes);

public sealed record CourtUpsertRequest(
    Guid? Id,
    string Name,
    bool IsActive,
    bool IsCovered,
    string FloorType,
    string WallType,
    decimal FullMatchPrice,
    IReadOnlyCollection<ClubScheduleRequest> Schedules);

public sealed record CourtScheduleResponse(DayOfWeek DayOfWeek, TimeOnly OpensAt, TimeOnly ClosesAt, int SlotMinutes);

public sealed record CourtResponse(
    Guid Id,
    string Name,
    bool IsActive,
    bool IsCovered,
    string FloorType,
    string WallType,
    decimal FullMatchPrice,
    IReadOnlyCollection<CourtScheduleResponse> Schedules);

public sealed record ClubResponse(
    Guid Id,
    string Name,
    ClubStatus Status,
    string? Address,
    string? City,
    int CourtCount,
    decimal FullMatchPrice,
    IReadOnlyCollection<CourtResponse> Courts);

public sealed record AvailabilityResponse(Guid CourtId, string CourtName, Guid ClubId, string ClubName, DateTime StartsAtUtc, DateTime EndsAtUtc, decimal Price);

public sealed record CreateMatchRequest(Guid CourtId, DateTime StartsAtUtc, int DurationMinutes);

public sealed record MatchResponse(
    Guid Id,
    string ClubName,
    string CourtName,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    MatchStatus Status,
    SkillCategory RequiredCategory,
    SkillLevel RequiredLevel,
    int PlayerCount,
    bool CanJoinDirectly,
    bool IsCreator,
    Guid? CurrentUserPaymentId,
    PaymentStatus? CurrentUserPaymentStatus,
    string? CurrentUserPaymentCheckoutUrl,
    IReadOnlyCollection<MatchPlayerResponse> Players);

public sealed record MatchPlayerResponse(string UserId, string FullName, int TeamNumber, SkillCategory Category, SkillLevel Level);

public sealed record JoinRequestDto(Guid Id, Guid MatchId, string UserId, string FullName, JoinRequestStatus Status, string? Message);

public sealed record CreateJoinRequestRequest(string? Message);

public sealed record PaymentPreferenceRequest(Guid MatchId);

public sealed record PaymentPreferenceResponse(
    Guid PaymentId,
    string PreferenceId,
    string CheckoutUrl,
    PaymentStatus Status,
    decimal Amount,
    decimal OwnerAmount,
    decimal AdminFeeAmount,
    decimal ProcessingReserveAmount);

public sealed record PlayerPaymentMethodResponse(
    bool HasPaymentMethod,
    bool CanReserveAutomatically,
    string? LinkType,
    string? MercadoPagoCustomerId,
    string? MercadoPagoCardId,
    string? PaymentMethodId,
    string? CardBrand,
    string? LastFourDigits,
    DateTime? LinkedAtUtc);

public sealed record UpsertPlayerPaymentMethodRequest(
    string? MercadoPagoCustomerId,
    string? MercadoPagoCardId,
    string? CardToken,
    string PaymentMethodId,
    string? CardBrand,
    string? LastFourDigits);

public sealed record PlayerPaymentConfigResponse(
    MercadoPagoEnvironment Environment,
    string? PublicKey,
    bool CanTokenizeCards,
    bool CanConnectMercadoPagoAccount);

public sealed record MercadoPagoSettingsResponse(
    MercadoPagoEnvironment Environment,
    string? PublicKey,
    string? OAuthClientId,
    string? OAuthRedirectUrl,
    bool HasAccessToken,
    bool HasOAuthClientSecret,
    string? SuccessUrl,
    string? FailureUrl,
    string? PendingUrl,
    string? NotificationUrl);

public sealed record UpdateMercadoPagoSettingsRequest(
    MercadoPagoEnvironment Environment,
    string? PublicKey,
    string? AccessToken,
    string? OAuthClientId,
    string? OAuthClientSecret,
    string? OAuthRedirectUrl,
    string? SuccessUrl,
    string? FailureUrl,
    string? PendingUrl,
    string? NotificationUrl);

public sealed record MercadoPagoWebhookData(string? Id);

public sealed record MercadoPagoWebhookRequest(
    string? ProviderPaymentId,
    PaymentStatus? Status,
    string? Type,
    string? Action,
    MercadoPagoWebhookData? Data);

public sealed record NotificationResponse(Guid Id, NotificationType Type, string Title, string Message, bool IsRead, DateTime CreatedAtUtc);
