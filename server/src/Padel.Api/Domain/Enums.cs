namespace Padel.Api.Domain;

public enum SkillCategory
{
    Octava = 1,
    Septima = 2,
    Sexta = 3,
    Quinta = 4,
    Cuarta = 5,
    Tercera = 6,
    Segunda = 7,
    Primera = 8
}

public enum SkillLevel
{
    Bajo = 1,
    Medio = 2,
    Alto = 3
}

public enum MatchStatus
{
    Open = 1,
    Full = 2,
    Cancelled = 3,
    Completed = 4
}

public enum JoinRequestStatus
{
    Pending = 1,
    Accepted = 2,
    Rejected = 3,
    Cancelled = 4
}

public enum ClubStatus
{
    PendingApproval = 1,
    Approved = 2,
    Rejected = 3
}

public enum PaymentStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Refunded = 4,
    Authorized = 5,
    Captured = 6,
    Cancelled = 7,
    Reserved = 8,
    Due = 9
}

public enum MercadoPagoEnvironment
{
    Sandbox = 1,
    Production = 2
}

public enum NotificationType
{
    MatchCreated = 1,
    JoinRequestCreated = 2,
    JoinRequestAccepted = 3,
    MatchFull = 4,
    MatchCancelled = 5,
    CourtApproved = 6,
    PaymentUpdated = 7,
    PlayerLeft = 8
}
