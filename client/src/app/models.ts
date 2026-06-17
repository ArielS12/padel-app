export type SkillCategory = 'Octava' | 'Septima' | 'Sexta' | 'Quinta' | 'Cuarta' | 'Tercera' | 'Segunda' | 'Primera';
export type SkillLevel = 'Bajo' | 'Medio' | 'Alto';
export type ClubStatus = 'PendingApproval' | 'Approved' | 'Rejected';
export type MatchStatus = 'Open' | 'Full' | 'Cancelled' | 'Completed';
export type JoinRequestStatus = 'Pending' | 'Accepted' | 'Rejected' | 'Cancelled';
export type PaymentStatus = 'Pending' | 'Approved' | 'Rejected' | 'Refunded' | 'Authorized' | 'Captured' | 'Cancelled' | 'Reserved' | 'Due';
export type MercadoPagoEnvironment = 'Sandbox' | 'Production';

export interface UserSummary {
  id: string;
  email: string;
  fullName: string;
  category: SkillCategory;
  level: SkillLevel;
  profilePhotoUrl?: string;
}

export interface AuthResponse {
  token: string;
  user: UserSummary;
}

export interface ProfileResponse extends UserSummary {
  city?: string;
  phone?: string;
  bio?: string;
  followers: number;
  following: number;
}

export interface OwnerAccountResponse {
  email: string;
  fullName: string;
  phone?: string;
  mercadoPagoAccountEmail?: string;
  mercadoPagoPublicKey?: string;
  mercadoPagoUserId?: string;
  mercadoPagoLinkedAtUtc?: string;
  hasMercadoPagoAccessToken: boolean;
}

export interface MercadoPagoConnectResponse {
  authorizationUrl: string;
}

export interface ClubScheduleRequest {
  dayOfWeek: number;
  opensAt: string;
  closesAt: string;
  slotMinutes: number;
}

export interface CourtResponse {
  id: string;
  name: string;
  isActive: boolean;
  isCovered: boolean;
  floorType: string;
  wallType: string;
  fullMatchPrice: number;
  schedules: ClubScheduleRequest[];
}

export interface ClubResponse {
  id: string;
  name: string;
  status: ClubStatus;
  address?: string;
  city?: string;
  courtCount: number;
  fullMatchPrice: number;
  mercadoPagoPublicKey?: string;
  courts: CourtResponse[];
}

export interface AvailabilityResponse {
  courtId: string;
  courtName: string;
  clubId: string;
  clubName: string;
  startsAtUtc: string;
  endsAtUtc: string;
  price: number;
}

export interface MatchPlayerResponse {
  userId: string;
  fullName: string;
  teamNumber: number;
  category: SkillCategory;
  level: SkillLevel;
}

export interface MatchResponse {
  id: string;
  clubName: string;
  courtName: string;
  startsAtUtc: string;
  endsAtUtc: string;
  status: MatchStatus;
  requiredCategory: SkillCategory;
  requiredLevel: SkillLevel;
  playerCount: number;
  canJoinDirectly: boolean;
  isCreator: boolean;
  currentUserPaymentId?: string;
  currentUserPaymentStatus?: PaymentStatus;
  currentUserPaymentCheckoutUrl?: string;
  players: MatchPlayerResponse[];
}

export interface JoinRequestDto {
  id: string;
  matchId: string;
  userId: string;
  fullName: string;
  status: JoinRequestStatus;
  message?: string;
}

export interface PaymentPreferenceResponse {
  paymentId: string;
  preferenceId: string;
  checkoutUrl: string;
  status: PaymentStatus;
  amount: number;
  ownerAmount: number;
  adminFeeAmount: number;
  processingReserveAmount: number;
}

export interface PlayerPaymentMethodResponse {
  hasPaymentMethod: boolean;
  canReserveAutomatically: boolean;
  linkType?: string;
  mercadoPagoCustomerId?: string;
  mercadoPagoCardId?: string;
  paymentMethodId?: string;
  cardBrand?: string;
  lastFourDigits?: string;
  linkedAtUtc?: string;
}

export interface PlayerPaymentConfigResponse {
  environment: MercadoPagoEnvironment;
  publicKey?: string;
  canTokenizeCards: boolean;
  canConnectMercadoPagoAccount: boolean;
}

export interface MercadoPagoSettingsResponse {
  environment: MercadoPagoEnvironment;
  publicKey?: string;
  oauthClientId?: string;
  oauthRedirectUrl?: string;
  hasAccessToken: boolean;
  hasOAuthClientSecret: boolean;
  successUrl?: string;
  failureUrl?: string;
  pendingUrl?: string;
  notificationUrl?: string;
}

export interface NotificationResponse {
  id: string;
  type: string;
  title: string;
  message: string;
  isRead: boolean;
  createdAtUtc: string;
}
