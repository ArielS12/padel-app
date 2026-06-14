import { CommonModule } from '@angular/common';
import { AfterViewInit, Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from './api.service';
import { googleAuthConfig } from './google-auth.config';
import { LoadingService } from './loading.service';
import {
  AvailabilityResponse,
  ClubScheduleRequest,
  ClubResponse,
  JoinRequestDto,
  MatchResponse,
  MercadoPagoEnvironment,
  MercadoPagoSettingsResponse,
  NotificationResponse,
  OwnerAccountResponse,
  PaymentPreferenceResponse,
  PlayerPaymentConfigResponse,
  PlayerPaymentMethodResponse,
  ProfileResponse,
  SkillCategory,
  SkillLevel
} from './models';

type DashboardSection = 'create' | 'available' | 'mine' | 'profile' | 'club' | 'admin' | 'payments';
type AuthMode = 'login' | 'register' | 'forgot';
type PlayerSection = Extract<DashboardSection, 'create' | 'available' | 'mine' | 'profile'>;

type CourtForm = {
  id?: string;
  name: string;
  isActive: boolean;
  isCovered: boolean;
  floorType: string;
  wallType: string;
  fullMatchPrice: number;
  schedules: ClubScheduleRequest[];
};

type GoogleCredentialResponse = {
  credential?: string;
};

type MercadoPagoCardToken = {
  id: string;
  last_four_digits?: string;
  payment_method_id?: string;
};

type MercadoPagoSecureFields = {
  create: (field: 'cardNumber' | 'expirationDate' | 'securityCode', options: { placeholder?: string; style?: Record<string, unknown>; }) => { mount: (id: string) => void; };
  createCardToken: (payload: {
    cardholderName: string;
    identificationType: string;
    identificationNumber: string;
  }) => Promise<MercadoPagoCardToken>;
};

type MercadoPagoInstance = {
  fields: MercadoPagoSecureFields;
};

declare global {
  interface Window {
    google?: {
      accounts: {
        id: {
          initialize: (config: { client_id: string; callback: (response: GoogleCredentialResponse) => void; auto_select?: boolean; }) => void;
          renderButton: (parent: HTMLElement, options: { theme: string; size: string; text: string; shape: string; width?: number; }) => void;
        };
      };
    };
    MercadoPago?: new (publicKey: string) => MercadoPagoInstance;
  }
}

@Component({
  selector: 'app-root',
  imports: [CommonModule, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App implements OnInit, AfterViewInit {
  readonly categories: SkillCategory[] = ['Octava', 'Septima', 'Sexta', 'Quinta', 'Cuarta', 'Tercera', 'Segunda', 'Primera'];
  readonly levels: SkillLevel[] = ['Bajo', 'Medio', 'Alto'];

  token = localStorage.getItem('padel_token');
  profile?: ProfileResponse;
  clubs: ClubResponse[] = [];
  availability: AvailabilityResponse[] = [];
  matches: MatchResponse[] = [];
  myMatches: MatchResponse[] = [];
  joinRequests: JoinRequestDto[] = [];
  notifications: NotificationResponse[] = [];
  pendingClubs: ClubResponse[] = [];
  ownerClubs: ClubResponse[] = [];
  ownerAccount?: OwnerAccountResponse;
  playerPaymentConfig?: PlayerPaymentConfigResponse;
  playerPaymentMethod?: PlayerPaymentMethodResponse;
  mercadoPagoSettings?: MercadoPagoSettingsResponse;
  lastPayment?: PaymentPreferenceResponse;
  message = '';
  error = '';
  isSavingPlayerCard = false;
  private mercadoPagoFields?: MercadoPagoSecureFields;
  private mercadoPagoFieldsPublicKey?: string;
  authMode: AuthMode = 'login';
  showAllMatches = false;
  activeSection: DashboardSection = 'create';
  isAdmin = this.token ? this.hasRole(this.token, 'Admin') : false;
  isClubOwner = this.token ? this.hasRole(this.token, 'ClubOwner') : false;
  readonly mercadoPagoEnvironments: MercadoPagoEnvironment[] = ['Sandbox', 'Production'];
  readonly floorTypes = ['Cesped sintetico', 'Cemento', 'Moqueta', 'Otro'];
  readonly wallTypes = ['Vidrio', 'Muro', 'Mixta', 'Sin pared'];
  readonly weekDays = [
    { value: 0, label: 'Domingo' },
    { value: 1, label: 'Lunes' },
    { value: 2, label: 'Martes' },
    { value: 3, label: 'Miercoles' },
    { value: 4, label: 'Jueves' },
    { value: 5, label: 'Viernes' },
    { value: 6, label: 'Sabado' }
  ];

  registerForm = {
    email: '',
    password: '',
    fullName: '',
    category: 'Octava' as SkillCategory,
    level: 'Bajo' as SkillLevel,
    city: '',
    phone: ''
  };

  loginForm = { email: '', password: '' };
  forgotPasswordForm = { email: '' };
  ownerRegisterForm = { email: '', password: '', fullName: '', phone: '' };
  googleForm = { category: 'Octava' as SkillCategory, level: 'Bajo' as SkillLevel };
  googleClientId = googleAuthConfig.clientId;

  profileForm = {
    fullName: '',
    city: '',
    phone: '',
    bio: '',
    profilePhotoUrl: '',
    category: 'Octava' as SkillCategory,
    level: 'Bajo' as SkillLevel
  };

  clubForm = { name: '' };
  clubDetailsForm = {
    clubId: '',
    address: '',
    city: '',
    courts: [this.createDefaultCourt()]
  };
  ownerAccountForm = {
    fullName: '',
    phone: '',
    currentPassword: '',
    newPassword: '',
    confirmNewPassword: '',
    mercadoPagoAccountEmail: '',
    mercadoPagoAccessToken: '',
    mercadoPagoPublicKey: ''
  };
  playerPaymentMethodForm = {
    cardholderName: '',
    cardNumber: '',
    expirationMonth: '',
    expirationYear: '',
    securityCode: '',
    identificationType: 'DNI',
    identificationNumber: '',
    paymentMethodId: 'visa',
    cardBrand: '',
    lastFourDigits: ''
  };
  mercadoPagoSettingsForm = {
    environment: 'Sandbox' as MercadoPagoEnvironment,
    publicKey: '',
    accessToken: '',
    oauthClientId: '',
    oauthClientSecret: '',
    oauthRedirectUrl: 'https://localhost:7128/api/account/club-owner/mercadopago/callback',
    successUrl: 'http://localhost:4200/pagos/exito',
    failureUrl: 'http://localhost:4200/pagos/error',
    pendingUrl: 'http://localhost:4200/pagos/pendiente',
    notificationUrl: 'https://localhost:7128/api/payments/mercadopago/webhook'
  };

  availabilityByStartForm = {
    startsAtLocal: this.defaultLocalDateTime(),
    durationMinutes: 90
  };

  availabilityByClubForm = {
    clubId: '',
    date: this.defaultDate()
  };

  createTurnForm = {
    clubId: '',
    courtId: '',
    date: this.defaultDate(),
    slotStartsAtUtc: ''
  };

  matchForm = {
    courtId: '',
    startsAtLocal: this.defaultLocalDateTime(),
    durationMinutes: 90
  };

  requestMessage = 'Me gustaria sumarme aunque estoy fuera del rango recomendado.';
  readonly loading$;

  get hasActiveMatch() {
    const now = Date.now();
    return this.myMatches.some(match =>
      (match.status === 'Open' || match.status === 'Full') &&
      new Date(match.endsAtUtc).getTime() > now);
  }

  get isPlayer() {
    return !this.isAdmin && !this.isClubOwner;
  }

  get courtsForCreateClub() {
    return this.clubs
      .find(club => club.id === this.createTurnForm.clubId)
      ?.courts
      .filter(court => court.isActive) ?? [];
  }

  get availableSlotsForSelectedCourt() {
    return this.availability.filter(slot =>
      slot.courtId === this.createTurnForm.courtId &&
      this.isFutureSlotForDeviceTime(slot));
  }

  get selectedCreateSlot() {
    return this.availableSlotsForSelectedCourt.find(slot => slot.startsAtUtc === this.createTurnForm.slotStartsAtUtc);
  }

  constructor(
    private readonly api: ApiService,
    loading: LoadingService
  ) {
    this.loading$ = loading.loading$;
    window.addEventListener('padel:invalid-token', () => this.handleInvalidSession());
  }

  ngOnInit() {
    if (this.token) {
      this.syncRoles(this.token);
      this.activeSection = this.defaultSectionForRole();
      this.refreshPrivateData();
    }
  }

  ngAfterViewInit() {
    this.renderGoogleButton();
  }

  register() {
    this.api.register(this.registerForm).subscribe({
      next: () => {
        this.loginForm.email = this.registerForm.email;
        this.loginForm.password = '';
        this.authMode = 'login';
        this.setMessage('Registro creado. Ahora inicia sesion para entrar al dashboard.');
      },
      error: error => this.showError(error)
    });
  }

  login() {
    this.api.login(this.loginForm.email, this.loginForm.password).subscribe({
      next: response => this.handleAuth(response.token, 'Sesion iniciada.'),
      error: error => this.showError(error)
    });
  }

  requestPasswordReset() {
    this.api.forgotPassword(this.forgotPasswordForm.email).subscribe({
      next: () => {
        this.authMode = 'login';
        this.loginForm.email = this.forgotPasswordForm.email;
        this.setMessage('Si el email existe, te enviamos instrucciones para recuperar tu contraseña.');
      },
      error: error => this.showError(error)
    });
  }

  registerClubOwner() {
    this.api.registerClubOwner(this.ownerRegisterForm).subscribe({
      next: owner => {
        this.ownerRegisterForm = { email: '', password: '', fullName: '', phone: '' };
        this.setMessage(`Dueño de cancha creado. Podra iniciar sesion con ${owner.email}.`);
      },
      error: error => this.showError(error)
    });
  }

  googleLogin(idToken: string) {
    this.api.googleLogin(idToken, this.googleForm.category, this.googleForm.level).subscribe({
      next: response => this.handleAuth(response.token, 'Sesion iniciada con Google.'),
      error: error => this.showError(error)
    });
  }

  logout() {
    localStorage.removeItem('padel_token');
    this.token = null;
    this.profile = undefined;
    this.matches = [];
    this.myMatches = [];
    this.notifications = [];
    this.joinRequests = [];
    this.pendingClubs = [];
    this.ownerClubs = [];
    this.ownerAccount = undefined;
    this.playerPaymentConfig = undefined;
    this.playerPaymentMethod = undefined;
    this.mercadoPagoSettings = undefined;
    this.resetMercadoPagoCardFields();
    this.clubDetailsForm = this.emptyClubDetailsForm();
    this.isAdmin = false;
    this.isClubOwner = false;
    this.activeSection = 'create';
    this.setMessage('Sesion cerrada.');
    window.setTimeout(() => this.renderGoogleButton());
  }

  setSection(section: DashboardSection) {
    if (this.isPlayerSection(section) && !this.isPlayer) {
      this.activeSection = this.defaultSectionForRole();
      return;
    }

    if (section === 'club' && !this.isClubOwner) {
      this.activeSection = this.isAdmin ? 'admin' : 'create';
      return;
    }

    if (section === 'admin' && !this.isAdmin) {
      this.activeSection = this.isClubOwner ? 'club' : 'create';
      return;
    }

    if (section === 'payments' && !this.isAdmin && !this.isClubOwner && !this.isPlayer) {
      this.activeSection = 'create';
      return;
    }

    if (section === 'create' && (this.isAdmin || this.isClubOwner)) {
      this.activeSection = this.isClubOwner ? 'club' : 'admin';
      return;
    }

    if (section === 'create' && this.hasActiveMatch) {
      this.activeSection = 'mine';
      this.setMessage('Ya tienes un turno activo. No puedes crear otro hasta que termine o se cancele.');
      return;
    }

    this.activeSection = section;
    if (section === 'create') {
      this.loadPublicData();
    }

    if (section === 'available') {
      this.searchMatches(false);
    }

    if (section === 'mine') {
      this.loadMyMatches();
      this.loadJoinRequests();
    }

    if (section === 'profile') {
      this.loadProfile();
      this.loadNotifications();
    }

    if (section === 'club') {
      this.loadOwnerClubs();
      this.loadOwnerAccount();
    }

    if (section === 'admin') {
      this.loadPendingClubs();
      this.loadMercadoPagoSettings();
    }

    if (section === 'payments') {
      if (this.isAdmin) {
        this.loadMercadoPagoSettings();
      }

      if (this.isClubOwner) {
        this.loadOwnerAccount();
      }

      if (this.isPlayer) {
        this.loadPlayerPaymentConfig();
        this.loadPlayerPaymentMethod();
      }
    }
  }

  loadProfile() {
    this.api.getProfile().subscribe({
      next: profile => {
        this.profile = profile;
        this.profileForm = {
          fullName: profile.fullName,
          city: profile.city ?? '',
          phone: profile.phone ?? '',
          bio: profile.bio ?? '',
          profilePhotoUrl: profile.profilePhotoUrl ?? '',
          category: profile.category,
          level: profile.level
        };
      },
      error: error => this.showError(error)
    });
  }

  updateProfile() {
    this.api.updateProfile(this.profileForm).subscribe({
      next: () => {
        this.setMessage('Perfil actualizado.');
        this.loadProfile();
      },
      error: error => this.showError(error)
    });
  }

  registerClub() {
    this.api.registerClub(this.clubForm.name).subscribe({
      next: club => {
        this.clubForm = { name: '' };
        this.selectClub(club);
        this.setMessage('Cancha registrada. Completa sus datos y espera aprobacion del administrador.');
        this.loadOwnerClubs();
      },
      error: error => this.showError(error)
    });
  }

  completeClub() {
    if (!this.clubDetailsForm.clubId) {
      const firstClub = this.ownerClubs[0];
      if (firstClub) {
        this.selectClub(firstClub);
      }
    }

    if (!this.clubDetailsForm.clubId) {
      this.showError({ message: 'Selecciona o registra un club antes de guardar sus canchas.' });
      return;
    }

    this.api.completeClub(this.clubDetailsForm.clubId, {
      address: this.clubDetailsForm.address,
      city: this.clubDetailsForm.city,
      courts: this.clubDetailsForm.courts.map(court => ({
        id: court.id,
        name: court.name,
        isActive: court.isActive,
        isCovered: court.isCovered,
        floorType: court.floorType,
        wallType: court.wallType,
        fullMatchPrice: Number(court.fullMatchPrice),
        schedules: court.schedules.map(schedule => ({
          dayOfWeek: Number(schedule.dayOfWeek),
          opensAt: schedule.opensAt,
          closesAt: schedule.closesAt,
          slotMinutes: Number(schedule.slotMinutes)
        }))
      }))
    }).subscribe({
      next: () => {
        this.setMessage('Datos de canchas guardados.');
        this.loadOwnerClubs();
      },
      error: error => this.showError(error)
    });
  }

  loadOwnerClubs() {
    if (!this.isClubOwner && !this.isAdmin) {
      return;
    }

    this.api.getMyClubs().subscribe({
      next: clubs => {
        this.ownerClubs = clubs;
        if (!clubs.length) {
          this.clubDetailsForm = this.emptyClubDetailsForm();
          return;
        }

        const selected = clubs.find(club => club.id === this.clubDetailsForm.clubId) ?? clubs[0];
        if (selected) {
          this.selectClub(selected);
        }
      },
      error: error => this.showError(error)
    });
  }

  selectClub(club: ClubResponse) {
    this.clubDetailsForm = {
      clubId: club.id,
      address: club.address ?? '',
      city: club.city ?? '',
      courts: club.courts.length
        ? club.courts.map(court => ({
          id: court.id,
          name: court.name,
          isActive: court.isActive,
          isCovered: court.isCovered,
          floorType: court.floorType || this.floorTypes[0],
          wallType: court.wallType || this.wallTypes[0],
          fullMatchPrice: court.fullMatchPrice || club.fullMatchPrice || 12000,
          schedules: court.schedules.length ? court.schedules.map(schedule => ({ ...schedule })) : [this.createDefaultSchedule()]
        }))
        : [this.createDefaultCourt()]
    };
  }

  selectClubById(clubId: string) {
    const club = this.ownerClubs.find(currentClub => currentClub.id === clubId);
    if (club) {
      this.selectClub(club);
      return;
    }

    this.clubDetailsForm.clubId = clubId;
  }

  addCourt() {
    this.clubDetailsForm.courts = [...this.clubDetailsForm.courts, this.createDefaultCourt(this.clubDetailsForm.courts.length + 1)];
  }

  removeCourt(index: number) {
    this.clubDetailsForm.courts = this.clubDetailsForm.courts.filter((_, currentIndex) => currentIndex !== index);
  }

  addCourtSchedule(court: CourtForm) {
    court.schedules = [...court.schedules, this.createDefaultSchedule()];
  }

  removeCourtSchedule(court: CourtForm, index: number) {
    court.schedules = court.schedules.filter((_, currentIndex) => currentIndex !== index);
  }

  loadOwnerAccount() {
    if (!this.isClubOwner && !this.isAdmin) {
      return;
    }

    this.api.getOwnerAccount().subscribe({
      next: account => {
        this.ownerAccount = account;
        this.ownerAccountForm = {
          fullName: account.fullName,
          phone: account.phone ?? '',
          currentPassword: '',
          newPassword: '',
          confirmNewPassword: '',
          mercadoPagoAccountEmail: account.mercadoPagoAccountEmail ?? '',
          mercadoPagoAccessToken: '',
          mercadoPagoPublicKey: account.mercadoPagoPublicKey ?? ''
        };
      },
      error: error => this.showError(error)
    });
  }

  updateOwnerAccount() {
    if (this.ownerAccountForm.newPassword !== this.ownerAccountForm.confirmNewPassword) {
      this.showError({ message: 'La nueva contraseña y su confirmacion no coinciden.' });
      return;
    }

    this.api.updateOwnerAccount({
      fullName: this.ownerAccountForm.fullName,
      phone: this.ownerAccountForm.phone,
      currentPassword: this.ownerAccountForm.currentPassword,
      newPassword: this.ownerAccountForm.newPassword,
      mercadoPagoAccountEmail: this.ownerAccountForm.mercadoPagoAccountEmail,
      mercadoPagoAccessToken: this.ownerAccountForm.mercadoPagoAccessToken,
      mercadoPagoPublicKey: this.ownerAccountForm.mercadoPagoPublicKey
    }).subscribe({
      next: account => {
        this.ownerAccount = account;
        this.ownerAccountForm.currentPassword = '';
        this.ownerAccountForm.newPassword = '';
        this.ownerAccountForm.confirmNewPassword = '';
        this.ownerAccountForm.mercadoPagoAccessToken = '';
        this.setMessage('Cuenta de cancha actualizada.');
      },
      error: error => this.showError(error)
    });
  }

  connectMercadoPagoAccount() {
    if (this.ownerAccount?.hasMercadoPagoAccessToken) {
      this.showError({ message: 'Primero debes desvincular la cuenta actual para conectar otra cuenta de Mercado Pago.' });
      return;
    }

    this.api.createMercadoPagoConnectUrl().subscribe({
      next: response => {
        window.open(response.authorizationUrl, '_blank', 'noopener,noreferrer');
        this.setMessage('Se abrio Mercado Pago para vincular la cuenta del dueño. Al terminar, vuelve y actualiza esta seccion.');
      },
      error: error => this.showError(error)
    });
  }

  disconnectMercadoPagoAccount() {
    if (!confirm('Vas a desvincular la cuenta de Mercado Pago del club. No se podran crear nuevos cobros hasta vincular otra cuenta.')) {
      return;
    }

    this.api.disconnectMercadoPagoAccount().subscribe({
      next: account => {
        this.ownerAccount = account;
        this.ownerAccountForm.mercadoPagoAccountEmail = '';
        this.ownerAccountForm.mercadoPagoAccessToken = '';
        this.ownerAccountForm.mercadoPagoPublicKey = '';
        this.setMessage('Cuenta de Mercado Pago desvinculada.');
      },
      error: error => this.showError(error)
    });
  }

  loadPlayerPaymentMethod() {
    if (!this.isPlayer) {
      return;
    }

    this.api.getPlayerPaymentMethod().subscribe({
      next: method => {
        this.playerPaymentMethod = method;
        this.playerPaymentMethodForm = {
          cardholderName: '',
          cardNumber: '',
          expirationMonth: '',
          expirationYear: '',
          securityCode: '',
          identificationType: 'DNI',
          identificationNumber: '',
          paymentMethodId: method.paymentMethodId ?? 'visa',
          cardBrand: method.cardBrand ?? '',
          lastFourDigits: method.lastFourDigits ?? ''
        };
      },
      error: error => this.showError(error)
    });
  }

  loadPlayerPaymentConfig() {
    if (!this.isPlayer) {
      return;
    }

    this.api.getPlayerPaymentConfig().subscribe({
      next: config => {
        this.playerPaymentConfig = config;
        if (config.canTokenizeCards) {
          window.setTimeout(() => this.initializeMercadoPagoCardFields());
        }
      },
      error: error => this.showError(error)
    });
  }

  connectPlayerMercadoPagoAccount() {
    this.api.createPlayerMercadoPagoConnectUrl().subscribe({
      next: response => {
        window.open(response.authorizationUrl, '_blank', 'noopener,noreferrer');
        this.setMessage('Se abrio Mercado Pago para vincular tu cuenta. Al terminar, vuelve y actualiza esta seccion.');
      },
      error: error => this.showError(error)
    });
  }

  async savePlayerCardPaymentMethod() {
    if (!this.playerPaymentConfig?.canTokenizeCards || !this.playerPaymentConfig.publicKey) {
      this.showError({ message: 'El administrador debe configurar la Public Key de Mercado Pago para cargar tarjetas.' });
      return;
    }

    this.isSavingPlayerCard = true;
    this.setMessage('Tokenizando tarjeta con Mercado Pago...');
    try {
      await this.initializeMercadoPagoCardFields();
      if (!this.mercadoPagoFields) {
        throw new Error('No se pudieron inicializar los campos seguros de Mercado Pago.');
      }

      const token = await this.mercadoPagoFields.createCardToken({
        cardholderName: this.playerPaymentMethodForm.cardholderName,
        identificationType: this.playerPaymentMethodForm.identificationType,
        identificationNumber: this.playerPaymentMethodForm.identificationNumber
      });

      this.setMessage('Tarjeta tokenizada. Guardando medio de pago...');
      this.api.updatePlayerPaymentMethod({
        cardToken: token.id,
        paymentMethodId: token.payment_method_id ?? this.playerPaymentMethodForm.paymentMethodId,
        cardBrand: this.playerPaymentMethodForm.cardBrand || token.payment_method_id || 'Tarjeta',
        lastFourDigits: token.last_four_digits ?? ''
      }).subscribe({
        next: method => {
          this.isSavingPlayerCard = false;
          this.playerPaymentMethod = method;
          this.clearPlayerCardForm();
          this.setMessage('Tarjeta agregada correctamente.');
        },
        error: error => {
          this.isSavingPlayerCard = false;
          this.showError(error);
        }
      });
    } catch (error) {
      this.isSavingPlayerCard = false;
      this.showError(error);
    }
  }

  deletePlayerPaymentMethod() {
    if (!confirm('Vas a eliminar tu tarjeta. No podras crear ni unirte a turnos hasta agregar una nueva.')) {
      return;
    }

    this.api.deletePlayerPaymentMethod().subscribe({
      next: method => {
        this.playerPaymentMethod = method;
        this.playerPaymentMethodForm = {
          cardholderName: '',
          cardNumber: '',
          expirationMonth: '',
          expirationYear: '',
          securityCode: '',
          identificationType: 'DNI',
          identificationNumber: '',
          paymentMethodId: 'visa',
          cardBrand: '',
          lastFourDigits: ''
        };
        this.resetMercadoPagoCardFields();
        window.setTimeout(() => this.initializeMercadoPagoCardFields());
        this.setMessage('Tarjeta eliminada.');
      },
      error: error => this.showError(error)
    });
  }

  loadAvailableByStart() {
    this.api.getAvailableByStart(this.toUtcIso(this.availabilityByStartForm.startsAtLocal), this.availabilityByStartForm.durationMinutes).subscribe({
      next: availability => {
        this.availability = availability;
        this.setMessage(`${availability.length} canchas disponibles para ese horario.`);
      },
      error: error => this.showError(error)
    });
  }

  loadAvailableByClub() {
    this.api.getAvailableByClub(this.availabilityByClubForm.clubId, this.availabilityByClubForm.date).subscribe({
      next: availability => {
        this.availability = availability;
        this.setMessage(`${availability.length} horarios disponibles para esa cancha.`);
      },
      error: error => this.showError(error)
    });
  }

  onCreateClubChange(clubId: string) {
    this.createTurnForm.clubId = clubId;
    this.createTurnForm.slotStartsAtUtc = '';
    this.availability = [];

    const firstCourt = this.courtsForCreateClub[0];
    this.createTurnForm.courtId = firstCourt?.id ?? '';
    if (this.createTurnForm.courtId) {
      this.loadAvailableForCreate();
    }
  }

  onCreateCourtChange(courtId: string) {
    this.createTurnForm.courtId = courtId;
    this.createTurnForm.slotStartsAtUtc = '';
    this.loadAvailableForCreate();
  }

  onCreateDateChange(date: string) {
    this.createTurnForm.date = date;
    this.createTurnForm.slotStartsAtUtc = '';
    if (this.createTurnForm.clubId && this.createTurnForm.courtId) {
      this.loadAvailableForCreate();
    }
  }

  loadAvailableForCreate() {
    if (!this.createTurnForm.clubId || !this.createTurnForm.courtId || !this.createTurnForm.date) {
      this.showError({ message: 'Selecciona dia, club y cancha para ver horarios disponibles.' });
      return;
    }

    this.api.getAvailableByClub(this.createTurnForm.clubId, this.createTurnForm.date).subscribe({
      next: availability => {
        this.availability = availability;
        if (this.createTurnForm.slotStartsAtUtc && !this.selectedCreateSlot) {
          this.createTurnForm.slotStartsAtUtc = '';
        }

        const slots = this.availableSlotsForSelectedCourt.length;
        this.setMessage(slots
          ? `${slots} horarios disponibles para esa cancha.`
          : 'No hay horarios disponibles para esa cancha en ese dia.');
      },
      error: error => this.showError(error)
    });
  }

  chooseCreateSlot(startsAtUtc: string) {
    const slot = this.availableSlotsForSelectedCourt.find(currentSlot => currentSlot.startsAtUtc === startsAtUtc);
    if (slot) {
      this.chooseSlot(slot);
    }
  }

  chooseSlot(slot: AvailabilityResponse) {
    this.matchForm.courtId = slot.courtId;
    this.matchForm.startsAtLocal = this.toLocalInputValue(slot.startsAtUtc);
    this.matchForm.durationMinutes = Math.round((new Date(slot.endsAtUtc).getTime() - new Date(slot.startsAtUtc).getTime()) / 60000);
    this.setMessage(`Seleccionaste ${slot.clubName} - ${slot.courtName}.`);
  }

  createMatch() {
    if (!this.ensurePlayerPaymentMethodConfigured()) {
      return;
    }

    if (this.hasActiveMatch) {
      this.activeSection = 'mine';
      this.setMessage('Ya tienes un turno activo. No puedes crear otro hasta que termine o se cancele.');
      return;
    }

    const selectedSlot = this.selectedCreateSlot;
    if (!selectedSlot) {
      this.showError({ message: 'Selecciona un horario disponible antes de crear el turno.' });
      return;
    }

    if (!this.isFutureSlotForDeviceTime(selectedSlot)) {
      this.createTurnForm.slotStartsAtUtc = '';
      this.showError({ message: 'Ese horario ya no esta disponible segun la hora actual de tu dispositivo.' });
      return;
    }

    const durationMinutes = Math.round((new Date(selectedSlot.endsAtUtc).getTime() - new Date(selectedSlot.startsAtUtc).getTime()) / 60000);
    this.api.createMatch(selectedSlot.courtId, selectedSlot.startsAtUtc, durationMinutes).subscribe({
      next: match => {
        if (match.currentUserPaymentCheckoutUrl) {
          this.setMessage('Turno creado. Redirigiendo a Mercado Pago para autorizar la reserva.');
          window.location.assign(match.currentUserPaymentCheckoutUrl);
          return;
        }

        this.setMessage('Turno creado y pago reservado correctamente.');
        this.activeSection = 'mine';
        this.refreshPrivateData();
      },
      error: error => this.showError(error)
    });
  }

  searchMatches(all = this.showAllMatches) {
    this.api.searchMatches(all).subscribe({
      next: matches => {
        this.matches = matches;
        this.showAllMatches = all;
      },
      error: error => this.showError(error)
    });
  }

  loadMyMatches() {
    this.api.getMyMatches().subscribe({
      next: matches => {
        this.myMatches = matches;
        if (this.hasActiveMatch && this.activeSection === 'create') {
          this.activeSection = 'mine';
        }
      },
      error: error => this.showError(error)
    });
  }

  joinMatch(match: MatchResponse) {
    if (!this.ensurePlayerPaymentMethodConfigured()) {
      return;
    }

    this.api.joinMatch(match.id).subscribe({
      next: updatedMatch => {
        if (updatedMatch.currentUserPaymentCheckoutUrl) {
          this.setMessage('Te uniste al turno. Redirigiendo a Mercado Pago para autorizar la reserva.');
          window.location.assign(updatedMatch.currentUserPaymentCheckoutUrl);
          return;
        }

        this.setMessage('Te uniste al turno y tu pago quedo reservado.');
        this.searchMatches(this.showAllMatches);
        this.loadMyMatches();
        this.loadNotifications();
      },
      error: error => this.showError(error)
    });
  }

  requestJoin(match: MatchResponse) {
    this.api.requestJoin(match.id, this.requestMessage).subscribe({
      next: () => this.setMessage('Solicitud enviada al creador del turno.'),
      error: error => this.showError(error)
    });
  }

  loadJoinRequests() {
    this.api.pendingJoinRequests().subscribe({
      next: requests => this.joinRequests = requests,
      error: error => this.showError(error)
    });
  }

  acceptRequest(request: JoinRequestDto) {
    this.api.acceptRequest(request.id).subscribe({
      next: () => {
        this.setMessage('Solicitud aceptada.');
        this.refreshPrivateData();
      },
      error: error => this.showError(error)
    });
  }

  rejectRequest(request: JoinRequestDto) {
    this.api.rejectRequest(request.id).subscribe({
      next: () => {
        this.setMessage('Solicitud rechazada.');
        this.loadJoinRequests();
      },
      error: error => this.showError(error)
    });
  }

  cancelMatch(match: MatchResponse) {
    this.api.cancelMatch(match.id).subscribe({
      next: () => {
        this.setMessage('Turno cancelado y horario liberado.');
        this.searchMatches(this.showAllMatches);
        this.loadMyMatches();
      },
      error: error => this.showError(error)
    });
  }

  leaveMatch(match: MatchResponse) {
    if (!confirm('Vas a salir del turno. Si tenias un pago autorizado, se cancelara la autorizacion.')) {
      return;
    }

    this.api.leaveMatch(match.id).subscribe({
      next: () => {
        this.setMessage('Saliste del turno y se cancelo tu autorizacion de pago.');
        this.searchMatches(this.showAllMatches);
        this.loadMyMatches();
        this.loadNotifications();
      },
      error: error => this.showError(error)
    });
  }

  createPayment(match: MatchResponse, openCheckout = false) {
    this.api.createPaymentPreference(match.id).subscribe({
      next: payment => {
        this.lastPayment = payment;
        if (openCheckout) {
          window.open(payment.checkoutUrl, '_blank', 'noopener,noreferrer');
        }

        this.setMessage('Autorizacion de Mercado Pago creada. Completa el checkout para reservar tu parte del pago.');
        this.loadMyMatches();
      },
      error: error => this.showError(error)
    });
  }

  loadNotifications() {
    this.api.getNotifications().subscribe({
      next: notifications => this.notifications = notifications,
      error: error => this.showError(error)
    });
  }

  markNotificationRead(notification: NotificationResponse) {
    this.api.markNotificationRead(notification.id).subscribe({
      next: () => this.loadNotifications(),
      error: error => this.showError(error)
    });
  }

  loadPendingClubs() {
    this.api.getPendingClubs().subscribe({
      next: clubs => this.pendingClubs = clubs,
      error: error => {
        if ((error as { status?: number }).status === 401 || (error as { status?: number }).status === 403) {
          this.pendingClubs = [];
          return;
        }

        this.showError(error);
      }
    });
  }

  approveClub(club: ClubResponse) {
    this.api.approveClub(club.id).subscribe({
      next: () => {
        this.setMessage('Cancha aprobada.');
        this.loadPendingClubs();
        this.loadPublicData();
      },
      error: error => this.showError(error)
    });
  }

  rejectClub(club: ClubResponse) {
    this.api.rejectClub(club.id).subscribe({
      next: () => {
        this.setMessage('Cancha rechazada.');
        this.loadPendingClubs();
      },
      error: error => this.showError(error)
    });
  }

  loadMercadoPagoSettings() {
    if (!this.isAdmin) {
      return;
    }

    this.api.getMercadoPagoSettings().subscribe({
      next: settings => {
        this.mercadoPagoSettings = settings;
        this.mercadoPagoSettingsForm = {
          environment: settings.environment,
          publicKey: settings.publicKey ?? '',
          accessToken: '',
          oauthClientId: settings.oauthClientId ?? '',
          oauthClientSecret: '',
          oauthRedirectUrl: settings.oauthRedirectUrl ?? 'https://localhost:7128/api/account/club-owner/mercadopago/callback',
          successUrl: settings.successUrl ?? 'http://localhost:4200/pagos/exito',
          failureUrl: settings.failureUrl ?? 'http://localhost:4200/pagos/error',
          pendingUrl: settings.pendingUrl ?? 'http://localhost:4200/pagos/pendiente',
          notificationUrl: settings.notificationUrl ?? 'https://localhost:7128/api/payments/mercadopago/webhook'
        };
      },
      error: error => this.showError(error)
    });
  }

  updateMercadoPagoSettings() {
    this.api.updateMercadoPagoSettings({
      environment: this.mercadoPagoSettingsForm.environment,
      publicKey: this.mercadoPagoSettingsForm.publicKey,
      accessToken: this.mercadoPagoSettingsForm.accessToken,
      oauthClientId: this.mercadoPagoSettingsForm.oauthClientId,
      oauthClientSecret: this.mercadoPagoSettingsForm.oauthClientSecret,
      oauthRedirectUrl: this.mercadoPagoSettingsForm.oauthRedirectUrl,
      successUrl: this.mercadoPagoSettingsForm.successUrl,
      failureUrl: this.mercadoPagoSettingsForm.failureUrl,
      pendingUrl: this.mercadoPagoSettingsForm.pendingUrl,
      notificationUrl: this.mercadoPagoSettingsForm.notificationUrl
    }).subscribe({
      next: settings => {
        this.mercadoPagoSettings = settings;
        this.mercadoPagoSettingsForm.accessToken = '';
        this.mercadoPagoSettingsForm.oauthClientSecret = '';
        this.setMessage('Configuracion de Mercado Pago actualizada.');
      },
      error: error => this.showError(error)
    });
  }

  private handleAuth(token: string, message: string) {
    this.resetMercadoPagoCardFields();
    localStorage.setItem('padel_token', token);
    this.token = token;
    this.syncRoles(token);
    this.activeSection = this.defaultSectionForRole();
    this.setMessage(message);
    this.refreshPrivateData();
  }

  private renderGoogleButton() {
    if (this.token || !this.googleClientId) {
      return;
    }

    const button = document.getElementById('googleSignInButton');
    if (!button) {
      return;
    }

    if (!window.google?.accounts?.id) {
      window.setTimeout(() => this.renderGoogleButton(), 300);
      return;
    }

    button.innerHTML = '';
    window.google.accounts.id.initialize({
      client_id: this.googleClientId,
      callback: response => {
        if (!response.credential) {
          this.showError({ message: 'Google no devolvio una credencial valida.' });
          return;
        }

        this.googleLogin(response.credential);
      },
      auto_select: false
    });

    window.google.accounts.id.renderButton(button, {
      theme: 'outline',
      size: 'large',
      text: 'signin_with',
      shape: 'pill',
      width: 280
    });
  }

  private refreshPrivateData() {
    if (this.isClubOwner) {
      this.activeSection = 'club';
      this.loadOwnerClubs();
      this.loadOwnerAccount();
      return;
    }

    if (this.isAdmin) {
      this.activeSection = 'admin';
      this.loadPendingClubs();
      this.loadMercadoPagoSettings();
      return;
    }

    this.loadPublicData();
    this.loadProfile();
    this.searchMatches(false);
    this.loadMyMatches();
    this.loadJoinRequests();
    this.loadNotifications();
    this.loadPlayerPaymentConfig();
    this.loadPlayerPaymentMethod();
  }

  private loadPublicData() {
    this.api.getClubs().subscribe({
      next: clubs => {
        this.clubs = clubs;
        if (!this.availabilityByClubForm.clubId && clubs.length > 0) {
          this.availabilityByClubForm.clubId = clubs[0].id;
        }

        if (!this.createTurnForm.clubId && clubs.length > 0) {
          this.createTurnForm.clubId = clubs[0].id;
          this.createTurnForm.courtId = clubs[0].courts.find(court => court.isActive)?.id ?? '';
        }
      },
      error: error => this.showError(error)
    });
  }

  private setMessage(message: string) {
    this.message = message;
    this.error = '';
  }

  private showError(error: unknown) {
    const httpError = error as { status?: number; error?: { detail?: string; title?: string } | string; message?: string };
    if (httpError.status === 401) {
      this.handleInvalidSession();
      return;
    }

    this.error = typeof httpError.error === 'string'
      ? httpError.error
      : httpError.error?.detail ?? httpError.error?.title ?? httpError.message ?? 'Ocurrio un error.';
    this.message = '';
  }

  private loadMercadoPagoSdk() {
    if (window.MercadoPago) {
      return Promise.resolve();
    }

    return new Promise<void>((resolve, reject) => {
      const existingScript = document.querySelector<HTMLScriptElement>('script[src="https://sdk.mercadopago.com/js/v2"]');
      if (existingScript) {
        existingScript.addEventListener('load', () => resolve(), { once: true });
        existingScript.addEventListener('error', () => reject(new Error('No se pudo cargar el SDK de Mercado Pago.')), { once: true });
        return;
      }

      const script = document.createElement('script');
      script.src = 'https://sdk.mercadopago.com/js/v2';
      script.async = true;
      script.onload = () => resolve();
      script.onerror = () => reject(new Error('No se pudo cargar el SDK de Mercado Pago.'));
      document.head.appendChild(script);
    });
  }

  private async initializeMercadoPagoCardFields() {
    if (!this.playerPaymentConfig?.publicKey) {
      return;
    }

    const cardNumberContainer = document.getElementById('playerCardNumberField');
    const expirationContainer = document.getElementById('playerCardExpirationField');
    const securityCodeContainer = document.getElementById('playerCardSecurityCodeField');
    if (!cardNumberContainer || !expirationContainer || !securityCodeContainer) {
      return;
    }

    const hasMountedFields =
      cardNumberContainer.childElementCount > 0 &&
      expirationContainer.childElementCount > 0 &&
      securityCodeContainer.childElementCount > 0;

    if (this.mercadoPagoFields && this.mercadoPagoFieldsPublicKey === this.playerPaymentConfig.publicKey && hasMountedFields) {
      return;
    }

    await this.loadMercadoPagoSdk();
    if (!window.MercadoPago) {
      throw new Error('No se pudo cargar Mercado Pago.');
    }

    cardNumberContainer.innerHTML = '';
    expirationContainer.innerHTML = '';
    securityCodeContainer.innerHTML = '';

    const mp = new window.MercadoPago(this.playerPaymentConfig.publicKey);
    this.mercadoPagoFields = mp.fields;
    this.mercadoPagoFieldsPublicKey = this.playerPaymentConfig.publicKey;
    const style = {
      fontSize: '16px',
      color: '#111827',
      fontFamily: 'inherit'
    };

    this.mercadoPagoFields.create('cardNumber', { placeholder: 'Numero de tarjeta', style }).mount('playerCardNumberField');
    this.mercadoPagoFields.create('expirationDate', { placeholder: 'MM/AA', style }).mount('playerCardExpirationField');
    this.mercadoPagoFields.create('securityCode', { placeholder: 'CVV', style }).mount('playerCardSecurityCodeField');
  }

  private resetMercadoPagoCardFields() {
    this.mercadoPagoFields = undefined;
    this.mercadoPagoFieldsPublicKey = undefined;
  }

  private clearPlayerCardForm() {
    this.playerPaymentMethodForm = {
      cardholderName: '',
      cardNumber: '',
      expirationMonth: '',
      expirationYear: '',
      securityCode: '',
      identificationType: 'DNI',
      identificationNumber: '',
      paymentMethodId: this.playerPaymentMethod?.paymentMethodId ?? 'visa',
      cardBrand: this.playerPaymentMethod?.cardBrand ?? '',
      lastFourDigits: this.playerPaymentMethod?.lastFourDigits ?? ''
    };
  }

  private onlyDigits(value: string) {
    return value.replace(/\D/g, '');
  }

  private ensurePlayerPaymentMethodConfigured() {
    if (this.playerPaymentMethod?.canReserveAutomatically) {
      return true;
    }

    this.activeSection = 'payments';
    this.loadPlayerPaymentConfig();
    this.loadPlayerPaymentMethod();
    this.showError({ message: 'Agrega una tarjeta en la seccion Pagos para poder autorizar tu lugar.' });
    return false;
  }

  private handleInvalidSession() {
    localStorage.removeItem('padel_token');
    this.token = null;
    this.profile = undefined;
    this.matches = [];
    this.myMatches = [];
    this.joinRequests = [];
    this.notifications = [];
    this.pendingClubs = [];
    this.ownerClubs = [];
    this.ownerAccount = undefined;
    this.playerPaymentConfig = undefined;
    this.playerPaymentMethod = undefined;
    this.mercadoPagoSettings = undefined;
    this.resetMercadoPagoCardFields();
    this.clubDetailsForm = this.emptyClubDetailsForm();
    this.isAdmin = false;
    this.isClubOwner = false;
    this.activeSection = 'create';
    this.error = '';
    this.message = 'Tu sesion expiro o no es valida. Inicia sesion nuevamente.';
    window.setTimeout(() => this.renderGoogleButton());
  }

  private hasRole(token: string, role: string) {
    try {
      const payload = JSON.parse(this.decodeJwtPart(token.split('.')[1])) as Record<string, unknown>;
      const roleClaims = [
        payload['role'],
        payload['roles'],
        payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role']
      ].flat();

      return roleClaims.some(claim => claim === role);
    } catch {
      return false;
    }
  }

  private syncRoles(token: string) {
    this.isAdmin = this.hasRole(token, 'Admin');
    this.isClubOwner = this.hasRole(token, 'ClubOwner');
  }

  private defaultSectionForRole(): DashboardSection {
    if (this.isClubOwner) {
      return 'club';
    }

    if (this.isAdmin) {
      return 'admin';
    }

    return 'create';
  }

  private isPlayerSection(section: DashboardSection): section is PlayerSection {
    return section === 'create' || section === 'available' || section === 'mine' || section === 'profile';
  }

  private decodeJwtPart(value: string) {
    const base64 = value.replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64.padEnd(Math.ceil(base64.length / 4) * 4, '=');
    return atob(padded);
  }

  private toUtcIso(localDateTime: string) {
    return new Date(localDateTime).toISOString();
  }

  private toLocalInputValue(dateTime: string) {
    const date = new Date(dateTime);
    date.setMinutes(date.getMinutes() - date.getTimezoneOffset());
    return date.toISOString().slice(0, 16);
  }

  private defaultLocalDateTime() {
    const date = new Date(Date.now() + 24 * 60 * 60 * 1000);
    date.setMinutes(date.getMinutes() - date.getTimezoneOffset());
    return date.toISOString().slice(0, 16);
  }

  private defaultDate() {
    const date = new Date(Date.now() + 24 * 60 * 60 * 1000);
    return date.toISOString().slice(0, 10);
  }

  private isFutureSlotForDeviceTime(slot: AvailabilityResponse) {
    return this.asDeviceLocalSlotDate(slot.startsAtUtc).getTime() > Date.now();
  }

  private asDeviceLocalSlotDate(dateTimeUtc: string) {
    const date = new Date(dateTimeUtc);
    return new Date(
      date.getUTCFullYear(),
      date.getUTCMonth(),
      date.getUTCDate(),
      date.getUTCHours(),
      date.getUTCMinutes(),
      date.getUTCSeconds(),
      date.getUTCMilliseconds());
  }

  private createDefaultCourt(index = 1): CourtForm {
    return {
      name: `Cancha ${index}`,
      isActive: true,
      isCovered: false,
      floorType: this.floorTypes[0],
      wallType: this.wallTypes[0],
      fullMatchPrice: 12000,
      schedules: [
        this.createDefaultSchedule(1),
        this.createDefaultSchedule(2),
        this.createDefaultSchedule(3),
        this.createDefaultSchedule(4),
        this.createDefaultSchedule(5)
      ]
    };
  }

  private createDefaultSchedule(dayOfWeek = 1): ClubScheduleRequest {
    return {
      dayOfWeek,
      opensAt: '08:00:00',
      closesAt: '23:00:00',
      slotMinutes: 90
    };
  }

  private emptyClubDetailsForm() {
    return {
      clubId: '',
      address: '',
      city: '',
      courts: [this.createDefaultCourt()]
    };
  }
}
