import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import {
  AuthResponse,
  AvailabilityResponse,
  ClubResponse,
  ClubScheduleRequest,
  CourtResponse,
  JoinRequestDto,
  MatchResponse,
  MercadoPagoConnectResponse,
  MercadoPagoEnvironment,
  MercadoPagoSettingsResponse,
  NotificationResponse,
  OwnerAccountResponse,
  PaymentPreferenceResponse,
  PaymentStatus,
  PlayerPaymentConfigResponse,
  PlayerPaymentMethodResponse,
  ProfileResponse,
  SkillCategory,
  SkillLevel,
  UserSummary
} from './models';

declare global {
  interface Window {
    padelConfig?: {
      apiBaseUrl?: string;
    };
  }
}

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly baseUrl = window.padelConfig?.apiBaseUrl ?? 'https://localhost:7128/api';

  constructor(private readonly http: HttpClient) {}

  register(payload: {
    email: string;
    password: string;
    fullName: string;
    category: SkillCategory;
    level: SkillLevel;
    city?: string;
    phone?: string;
  }) {
    return this.http.post<AuthResponse>(`${this.baseUrl}/auth/register`, payload);
  }

  registerClubOwner(payload: {
    email: string;
    password: string;
    fullName: string;
    phone?: string;
  }) {
    return this.http.post<UserSummary>(`${this.baseUrl}/auth/register-club-owner`, payload);
  }

  login(email: string, password: string) {
    return this.http.post<AuthResponse>(`${this.baseUrl}/auth/login`, { email, password });
  }

  forgotPassword(email: string) {
    return this.http.post<void>(`${this.baseUrl}/auth/forgot-password`, { email });
  }

  googleLogin(idToken: string, category: SkillCategory, level: SkillLevel) {
    return this.http.post<AuthResponse>(`${this.baseUrl}/auth/google`, { idToken, category, level });
  }

  getProfile(userId?: string) {
    return this.http.get<ProfileResponse>(`${this.baseUrl}/profile${userId ? `/${userId}` : ''}`);
  }

  updateProfile(payload: Partial<ProfileResponse>) {
    return this.http.put<void>(`${this.baseUrl}/profile`, payload);
  }

  follow(userId: string) {
    return this.http.post<void>(`${this.baseUrl}/profile/${userId}/follow`, {});
  }

  unfollow(userId: string) {
    return this.http.delete<void>(`${this.baseUrl}/profile/${userId}/follow`);
  }

  getClubs() {
    return this.http.get<ClubResponse[]>(`${this.baseUrl}/clubs`);
  }

  getMyClubs() {
    return this.http.get<ClubResponse[]>(`${this.baseUrl}/clubs/mine`);
  }

  registerClub(name: string) {
    return this.http.post<ClubResponse>(`${this.baseUrl}/clubs`, { name });
  }

  completeClub(clubId: string, payload: {
    address: string;
    city: string;
    courts: Array<Omit<CourtResponse, 'id' | 'schedules'> & { id?: string; schedules: ClubScheduleRequest[]; }>;
  }) {
    return this.http.put<ClubResponse>(`${this.baseUrl}/clubs/${clubId}/details`, payload);
  }

  getOwnerAccount() {
    return this.http.get<OwnerAccountResponse>(`${this.baseUrl}/account/club-owner`);
  }

  updateOwnerAccount(payload: {
    fullName: string;
    phone?: string;
    currentPassword?: string;
    newPassword?: string;
    mercadoPagoAccountEmail?: string;
    mercadoPagoAccessToken?: string;
    mercadoPagoPublicKey?: string;
  }) {
    return this.http.put<OwnerAccountResponse>(`${this.baseUrl}/account/club-owner`, payload);
  }

  createMercadoPagoConnectUrl() {
    return this.http.post<MercadoPagoConnectResponse>(`${this.baseUrl}/account/club-owner/mercadopago/connect`, {});
  }

  disconnectMercadoPagoAccount() {
    return this.http.delete<OwnerAccountResponse>(`${this.baseUrl}/account/club-owner/mercadopago`);
  }

  getAvailableByStart(startsAtUtc: string, durationMinutes: number) {
    const params = new HttpParams()
      .set('startsAtUtc', startsAtUtc)
      .set('durationMinutes', durationMinutes);
    return this.http.get<AvailabilityResponse[]>(`${this.baseUrl}/clubs/available-by-start`, { params });
  }

  getAvailableByClub(clubId: string, date: string) {
    const params = new HttpParams().set('date', date);
    return this.http.get<AvailabilityResponse[]>(`${this.baseUrl}/clubs/${clubId}/availability`, { params });
  }

  createMatch(courtId: string, startsAtUtc: string, durationMinutes: number, payment?: {
    cardToken: string;
    paymentMethodId: string;
    cardBrand?: string;
    lastFourDigits?: string;
  }) {
    return this.http.post<MatchResponse>(`${this.baseUrl}/matches`, { courtId, startsAtUtc, durationMinutes, ...payment });
  }

  searchMatches(all = false) {
    return this.http.get<MatchResponse[]>(`${this.baseUrl}/matches`, { params: new HttpParams().set('all', all) });
  }

  getMyMatches() {
    return this.http.get<MatchResponse[]>(`${this.baseUrl}/matches/mine`);
  }

  joinMatch(matchId: string) {
    return this.http.post<MatchResponse>(`${this.baseUrl}/matches/${matchId}/join`, {});
  }

  leaveMatch(matchId: string) {
    return this.http.post<void>(`${this.baseUrl}/matches/${matchId}/leave`, {});
  }

  requestJoin(matchId: string, message: string) {
    return this.http.post<JoinRequestDto>(`${this.baseUrl}/matches/${matchId}/requests`, { message });
  }

  pendingJoinRequests() {
    return this.http.get<JoinRequestDto[]>(`${this.baseUrl}/matches/requests/pending`);
  }

  acceptRequest(requestId: string) {
    return this.http.post<void>(`${this.baseUrl}/matches/requests/${requestId}/accept`, {});
  }

  rejectRequest(requestId: string) {
    return this.http.post<void>(`${this.baseUrl}/matches/requests/${requestId}/reject`, {});
  }

  cancelMatch(matchId: string) {
    return this.http.post<void>(`${this.baseUrl}/matches/${matchId}/cancel`, {});
  }

  createPaymentPreference(matchId: string) {
    return this.http.post<PaymentPreferenceResponse>(`${this.baseUrl}/payments/preferences`, { matchId });
  }

  getPlayerPaymentConfig() {
    return this.http.get<PlayerPaymentConfigResponse>(`${this.baseUrl}/player-payments/config`);
  }

  getPlayerPaymentMethod() {
    return this.http.get<PlayerPaymentMethodResponse>(`${this.baseUrl}/player-payments/method`);
  }

  updatePlayerPaymentMethod(payload: {
    paymentMethodId: string;
    cardBrand?: string;
    lastFourDigits?: string;
    cardholderName?: string;
    identificationType?: string;
    identificationNumber?: string;
  }) {
    return this.http.post<PlayerPaymentMethodResponse>(`${this.baseUrl}/player-payments/method`, payload);
  }

  createPlayerMercadoPagoConnectUrl() {
    return this.http.post<MercadoPagoConnectResponse>(`${this.baseUrl}/player-payments/mercadopago/connect`, {});
  }

  deletePlayerPaymentMethod() {
    return this.http.delete<PlayerPaymentMethodResponse>(`${this.baseUrl}/player-payments/method`);
  }

  deletePlayerCard() {
    return this.http.delete<PlayerPaymentMethodResponse>(`${this.baseUrl}/player-payments/method/card`);
  }

  updatePayment(providerPaymentId: string, status: PaymentStatus) {
    return this.http.post<void>(`${this.baseUrl}/payments/mercadopago/webhook`, { providerPaymentId, status });
  }

  getNotifications() {
    return this.http.get<NotificationResponse[]>(`${this.baseUrl}/notifications`);
  }

  markNotificationRead(notificationId: string) {
    return this.http.post<void>(`${this.baseUrl}/notifications/${notificationId}/read`, {});
  }

  getPendingClubs() {
    return this.http.get<ClubResponse[]>(`${this.baseUrl}/admin/clubs/pending`);
  }

  approveClub(clubId: string) {
    return this.http.post<void>(`${this.baseUrl}/admin/clubs/${clubId}/approve`, {});
  }

  rejectClub(clubId: string) {
    return this.http.post<void>(`${this.baseUrl}/admin/clubs/${clubId}/reject`, {});
  }

  getMercadoPagoSettings() {
    return this.http.get<MercadoPagoSettingsResponse>(`${this.baseUrl}/admin/mercadopago`);
  }

  updateMercadoPagoSettings(payload: {
    environment: MercadoPagoEnvironment;
    publicKey?: string;
    accessToken?: string;
    oauthClientId?: string;
    oauthClientSecret?: string;
    oauthRedirectUrl?: string;
    successUrl?: string;
    failureUrl?: string;
    pendingUrl?: string;
    notificationUrl?: string;
  }) {
    return this.http.put<MercadoPagoSettingsResponse>(`${this.baseUrl}/admin/mercadopago`, payload);
  }
}
