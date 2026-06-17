using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Padel.Api.Contracts;
using Padel.Api.Data;
using Padel.Api.Domain;
using Padel.Api.Services;

namespace Padel.Tests;

public sealed class DomainRulesTests
{
    [Fact]
    public void SkillMatcher_AllowsSameRankAndOneRankAboveOrBelow()
    {
        var matcher = new SkillMatcher();

        Assert.True(matcher.IsCompatible(SkillCategory.Sexta, SkillLevel.Bajo, SkillCategory.Septima, SkillLevel.Alto));
        Assert.True(matcher.IsCompatible(SkillCategory.Sexta, SkillLevel.Bajo, SkillCategory.Sexta, SkillLevel.Medio));
        Assert.True(matcher.IsCompatible(SkillCategory.Sexta, SkillLevel.Bajo, SkillCategory.Sexta, SkillLevel.Bajo));
        Assert.False(matcher.IsCompatible(SkillCategory.Sexta, SkillLevel.Bajo, SkillCategory.Sexta, SkillLevel.Alto));
    }

    [Fact]
    public async Task Availability_ReleasesCancelledSlots()
    {
        await using var db = CreateDbContext();
        var court = await SeedApprovedClubAsync(db);
        var service = new AvailabilityService(db);
        var startsAt = NextWeekdayUtc(DayOfWeek.Monday, 10);

        var booking = await service.BlockSlotAsync(court.Id, startsAt, 90, CancellationToken.None);
        var unavailable = await service.GetAvailableByStartAsync(startsAt, 90, CancellationToken.None);

        await service.ReleaseSlotAsync(booking.Id, CancellationToken.None);
        var available = await service.GetAvailableByStartAsync(startsAt, 90, CancellationToken.None);

        Assert.DoesNotContain(unavailable, slot => slot.CourtId == court.Id);
        Assert.Contains(available, slot => slot.CourtId == court.Id);
    }

    [Fact]
    public async Task MatchService_CancelsIncompleteMatchesTwoHoursBeforeStart()
    {
        await using var db = CreateDbContext();
        var court = await SeedApprovedClubAsync(db);
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "player@test.local",
            Email = "player@test.local",
            EmailConfirmed = true,
            FullName = "Player Test",
            Category = SkillCategory.Sexta,
            Level = SkillLevel.Bajo
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var notifications = new FakeNotifications();
        var service = new MatchService(db, new SkillMatcher(), new AvailabilityService(db), notifications, new FakeMercadoPagoService(db));
        var startsAt = NextWeekdayUtc(DayOfWeek.Monday, 10);
        var match = await service.CreateAsync(user, new CreateMatchRequest(court.Id, startsAt, 90), CancellationToken.None);
        db.Payments.Add(new Payment
        {
            MatchId = match.Id,
            UserId = user.Id,
            Amount = 2500m,
            OwnerAmount = 2325m,
            AdminFeeAmount = 75m,
            ProcessingReserveAmount = 100m,
            Status = PaymentStatus.Reserved
        });
        await db.SaveChangesAsync();

        var cancelled = await service.CancelIncompleteAsync(startsAt.AddMinutes(-90), CancellationToken.None);
        var booking = await db.CourtBookings.SingleAsync(x => x.Id == match.CourtBookingId);
        var payment = await db.Payments.SingleAsync();

        Assert.Equal(1, cancelled);
        Assert.Equal(MatchStatus.Cancelled, match.Status);
        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.True(booking.IsCancelled);
        Assert.Contains(match.Id, notifications.CancelledMatches);
    }

    [Fact]
    public async Task MatchService_PreventsCreatingAnotherActiveMatchForSameUser()
    {
        await using var db = CreateDbContext();
        var firstCourt = await SeedApprovedClubAsync(db);
        var secondCourt = await AddCourtAsync(db, firstCourt.ClubId, "Cancha 2");
        var user = CreateUser("player@test.local");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new MatchService(db, new SkillMatcher(), new AvailabilityService(db), new FakeNotifications(), new FakeMercadoPagoService(db));
        var startsAt = NextWeekdayUtc(DayOfWeek.Monday, 10);
        await service.CreateAsync(user, new CreateMatchRequest(firstCourt.Id, startsAt, 90), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(user, new CreateMatchRequest(secondCourt.Id, startsAt.AddHours(3), 90), CancellationToken.None));

        Assert.Equal("Ya tienes un turno activo. No puedes crear o unirte a otro hasta que termine o se cancele.", ex.Message);
    }

    [Fact]
    public async Task MatchService_PreventsJoiningAnotherActiveMatchForSameUser()
    {
        await using var db = CreateDbContext();
        var firstCourt = await SeedApprovedClubAsync(db);
        var secondCourt = await AddCourtAsync(db, firstCourt.ClubId, "Cancha 2");
        var player = CreateUser("player@test.local");
        var otherCreator = CreateUser("creator@test.local");
        db.Users.AddRange(player, otherCreator);
        await db.SaveChangesAsync();

        var service = new MatchService(db, new SkillMatcher(), new AvailabilityService(db), new FakeNotifications(), new FakeMercadoPagoService(db));
        var startsAt = NextWeekdayUtc(DayOfWeek.Monday, 10);
        await service.CreateAsync(player, new CreateMatchRequest(firstCourt.Id, startsAt, 90), CancellationToken.None);
        var secondMatch = await service.CreateAsync(otherCreator, new CreateMatchRequest(secondCourt.Id, startsAt.AddHours(3), 90), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.JoinAsync(secondMatch.Id, player, CancellationToken.None));

        Assert.Equal("Ya tienes un turno activo. No puedes crear o unirte a otro hasta que termine o se cancele.", ex.Message);
    }

    [Fact]
    public async Task MatchService_AllowsPlayerToLeaveAndCancelsReservedPayment()
    {
        await using var db = CreateDbContext();
        var court = await SeedApprovedClubAsync(db);
        var creator = CreateUser("creator@test.local");
        var leavingPlayer = CreateUser("leaving@test.local");
        var thirdPlayer = CreateUser("third@test.local");
        var fourthPlayer = CreateUser("fourth@test.local");
        db.Users.AddRange(creator, leavingPlayer, thirdPlayer, fourthPlayer);
        await db.SaveChangesAsync();

        var service = new MatchService(db, new SkillMatcher(), new AvailabilityService(db), new FakeNotifications(), new FakeMercadoPagoService(db));
        var startsAt = NextWeekdayUtc(DayOfWeek.Monday, 10);
        var match = await service.CreateAsync(creator, new CreateMatchRequest(court.Id, startsAt, 90), CancellationToken.None);
        await service.JoinAsync(match.Id, leavingPlayer, CancellationToken.None);
        await service.JoinAsync(match.Id, thirdPlayer, CancellationToken.None);
        await service.JoinAsync(match.Id, fourthPlayer, CancellationToken.None);

        db.Payments.Add(new Payment
        {
            MatchId = match.Id,
            UserId = leavingPlayer.Id,
            Amount = 2500m,
            OwnerAmount = 2325m,
            AdminFeeAmount = 75m,
            ProcessingReserveAmount = 100m,
            Status = PaymentStatus.Reserved
        });
        await db.SaveChangesAsync();

        await service.LeaveAsync(match.Id, leavingPlayer.Id, CancellationToken.None);

        var payment = await db.Payments.SingleAsync();
        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.Equal(MatchStatus.Open, match.Status);
        Assert.DoesNotContain(match.Players, player => player.UserId == leavingPlayer.Id);
    }

    [Fact]
    public async Task NotificationService_NotifiesCompatiblePlayersWhenMatchIsCreated()
    {
        await using var db = CreateDbContext();
        var playerRole = new IdentityRole("Player");
        var clubOwnerRole = new IdentityRole("ClubOwner");
        var creator = CreateUser("creator@test.local");
        var compatiblePlayer = CreateUser("compatible@test.local");
        compatiblePlayer.EmailConfirmed = false;
        var incompatiblePlayer = CreateUser("incompatible@test.local");
        incompatiblePlayer.Level = SkillLevel.Alto;
        var clubOwner = CreateUser("owner-role@test.local");

        db.Roles.AddRange(playerRole, clubOwnerRole);
        db.Users.AddRange(creator, compatiblePlayer, incompatiblePlayer, clubOwner);
        db.UserRoles.AddRange(
            new IdentityUserRole<string> { UserId = creator.Id, RoleId = playerRole.Id },
            new IdentityUserRole<string> { UserId = compatiblePlayer.Id, RoleId = playerRole.Id },
            new IdentityUserRole<string> { UserId = incompatiblePlayer.Id, RoleId = playerRole.Id },
            new IdentityUserRole<string> { UserId = clubOwner.Id, RoleId = clubOwnerRole.Id });
        await db.SaveChangesAsync();

        var match = new PadelMatch
        {
            Id = Guid.NewGuid(),
            CreatorId = creator.Id,
            RequiredCategory = SkillCategory.Sexta,
            RequiredLevel = SkillLevel.Bajo,
            StartsAtUtc = NextWeekdayUtc(DayOfWeek.Monday, 10)
        };

        await new NotificationService(db, new SkillMatcher()).NotifyEligibleMatchCreatedAsync(match, CancellationToken.None);

        var notifications = await db.Notifications.ToListAsync();
        Assert.Contains(notifications, notification => notification.UserId == compatiblePlayer.Id && notification.Type == NotificationType.MatchCreated);
        Assert.DoesNotContain(notifications, notification => notification.UserId == creator.Id);
        Assert.DoesNotContain(notifications, notification => notification.UserId == incompatiblePlayer.Id);
        Assert.DoesNotContain(notifications, notification => notification.UserId == clubOwner.Id);
    }

    [Fact]
    public async Task MercadoPagoService_CreatesCheckoutPreferenceWhenMatchIsCompleted()
    {
        await using var db = CreateDbContext();
        var court = await SeedApprovedClubAsync(db);
        var owner = await db.Users.SingleAsync(user => user.Email == "owner@test.local");
        owner.MercadoPagoAccessToken = "owner-token";
        var player = CreateUser("payer@test.local");
        var startsAt = NextWeekdayUtc(DayOfWeek.Monday, 10);
        var booking = new CourtBooking
        {
            CourtId = court.Id,
            StartsAtUtc = startsAt,
            EndsAtUtc = startsAt.AddMinutes(90)
        };
        db.Users.Add(player);
        db.CourtBookings.Add(booking);
        await db.SaveChangesAsync();

        var payment = new Payment
        {
            MatchId = Guid.NewGuid(),
            UserId = player.Id,
            Amount = 2500m,
            OwnerAmount = 2325m,
            AdminFeeAmount = 75m,
            ProcessingReserveAmount = 100m,
            Status = PaymentStatus.Due
        };
        var match = new PadelMatch
        {
            Id = payment.MatchId,
            CreatorId = player.Id,
            CourtId = court.Id,
            CourtBookingId = booking.Id,
            StartsAtUtc = booking.StartsAtUtc,
            EndsAtUtc = booking.EndsAtUtc,
            RequiredCategory = SkillCategory.Sexta,
            RequiredLevel = SkillLevel.Bajo,
            Status = MatchStatus.Completed,
            Players =
            {
                new MatchPlayer { UserId = player.Id, TeamNumber = 1 }
            },
            Payments = { payment }
        };
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var httpHandler = new FakeMercadoPagoHandler();
        var service = new MercadoPagoService(
            db,
            new HttpClient(httpHandler),
            Options.Create(new MercadoPagoOptions { SandboxPayerEmail = "buyer@testuser.com" }),
            new FakeNotifications());

        var preference = await service.CreatePreferenceAsync(player, match.Id, CancellationToken.None);

        Assert.Equal(2500m, preference.Amount);
        Assert.Equal(PaymentStatus.Pending, preference.Status);
        Assert.Contains("\"marketplace_fee\":75", httpHandler.LastRequestBody);
        Assert.Contains("\"email\":\"buyer@testuser.com\"", httpHandler.LastRequestBody);
        Assert.Contains($"\"external_reference\":\"{payment.Id}\"", httpHandler.LastRequestBody);
        Assert.EndsWith("/checkout/preferences", httpHandler.LastRequestPath);

        await service.SyncPaymentFromProviderAsync(payment.Id, "payment-test", CancellationToken.None);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal("payment-test", payment.ProviderPaymentId);
    }

    [Fact]
    public async Task MercadoPagoService_ReservesPlayerPaymentWithoutCallingMercadoPago()
    {
        await using var db = CreateDbContext();
        var court = await SeedApprovedClubAsync(db);
        var owner = await db.Users.SingleAsync(user => user.Email == "owner@test.local");
        owner.MercadoPagoAccessToken = "owner-token";
        var player = CreateUser("reserved-payer@test.local");
        var startsAt = NextWeekdayUtc(DayOfWeek.Monday, 10);
        var booking = new CourtBooking
        {
            CourtId = court.Id,
            StartsAtUtc = startsAt,
            EndsAtUtc = startsAt.AddMinutes(90)
        };
        db.Users.Add(player);
        db.CourtBookings.Add(booking);
        await db.SaveChangesAsync();

        var match = new PadelMatch
        {
            CreatorId = player.Id,
            CourtId = court.Id,
            CourtBookingId = booking.Id,
            StartsAtUtc = booking.StartsAtUtc,
            EndsAtUtc = booking.EndsAtUtc,
            RequiredCategory = SkillCategory.Sexta,
            RequiredLevel = SkillLevel.Bajo,
            Players =
            {
                new MatchPlayer { UserId = player.Id, TeamNumber = 1 }
            }
        };
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var httpHandler = new FakeMercadoPagoHandler();
        var service = new MercadoPagoService(
            db,
            new HttpClient(httpHandler),
            Options.Create(new MercadoPagoOptions { SandboxPayerEmail = "buyer@testuser.com" }),
            new FakeNotifications());

        var reservation = await service.ReservePlayerPaymentAsync(player, match.Id, CancellationToken.None);
        var payment = await db.Payments.SingleAsync();

        Assert.Equal(PaymentStatus.Reserved, reservation.Status);
        Assert.Equal(PaymentStatus.Reserved, payment.Status);
        Assert.Equal(2500m, payment.Amount);
        Assert.Null(payment.CheckoutUrl);
        Assert.Empty(httpHandler.LastRequestBody);
    }

    [Fact]
    public async Task MatchService_BlocksCreateWhenClubOwnerHasNoMercadoPago()
    {
        await using var db = CreateDbContext();
        var court = await SeedApprovedClubAsync(db);
        var player = CreateUser("without-owner-mp@test.local");
        db.Users.Add(player);
        await db.SaveChangesAsync();

        var paymentService = new MercadoPagoService(
            db,
            new HttpClient(new FakeMercadoPagoHandler()),
            Options.Create(new MercadoPagoOptions()),
            new FakeNotifications());
        var service = new MatchService(db, new SkillMatcher(), new AvailabilityService(db), new FakeNotifications(), paymentService);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(player, new CreateMatchRequest(court.Id, NextWeekdayUtc(DayOfWeek.Monday, 10), 90), CancellationToken.None));

        Assert.Contains("vincular Mercado Pago", ex.Message);
        Assert.Empty(db.Matches);
        Assert.All(db.CourtBookings, booking => Assert.True(booking.IsCancelled));
    }

    [Fact]
    public async Task MercadoPagoService_CompleteFinishedMatchesMarksPaymentsDue()
    {
        await using var db = CreateDbContext();
        var court = await SeedApprovedClubAsync(db);
        var owner = await db.Users.SingleAsync(user => user.Email == "owner@test.local");
        owner.MercadoPagoAccessToken = "owner-token";
        var player = CreateUser("payer@test.local");
        var startsAt = DateTime.UtcNow.AddHours(-2);
        var booking = new CourtBooking
        {
            CourtId = court.Id,
            StartsAtUtc = startsAt,
            EndsAtUtc = startsAt.AddMinutes(90)
        };
        db.Users.Add(player);
        db.CourtBookings.Add(booking);
        await db.SaveChangesAsync();

        var match = new PadelMatch
        {
            CreatorId = player.Id,
            CourtId = court.Id,
            CourtBookingId = booking.Id,
            StartsAtUtc = booking.StartsAtUtc,
            EndsAtUtc = booking.EndsAtUtc,
            RequiredCategory = SkillCategory.Sexta,
            RequiredLevel = SkillLevel.Bajo,
            Status = MatchStatus.Full,
            Payments =
            {
                new Payment
                {
                    UserId = player.Id,
                    Amount = 2500m,
                    OwnerAmount = 2325m,
                    AdminFeeAmount = 75m,
                    ProcessingReserveAmount = 100m,
                    Status = PaymentStatus.Reserved
                }
            }
        };
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var notifications = new FakeNotifications();
        var service = new MercadoPagoService(
            db,
            new HttpClient(new FakeMercadoPagoHandler()),
            Options.Create(new MercadoPagoOptions()),
            notifications);

        var completed = await service.CompleteFinishedMatchesAsync(DateTime.UtcNow, CancellationToken.None);
        var payment = await db.Payments.SingleAsync();

        Assert.Equal(1, completed);
        Assert.Equal(PaymentStatus.Due, payment.Status);
        Assert.Equal(MatchStatus.Completed, match.Status);
        Assert.Single(notifications.DuePayments);
    }

    [Fact]
    public async Task MercadoPagoService_UsesLinkedMercadoPagoEmailForCheckout()
    {
        await using var db = CreateDbContext();
        var court = await SeedApprovedClubAsync(db);
        var owner = await db.Users.SingleAsync(user => user.Email == "owner@test.local");
        owner.MercadoPagoAccessToken = "owner-token";
        var player = CreateUser("payer@test.local");
        player.Email = "registro@miapp.com";
        var startsAt = NextWeekdayUtc(DayOfWeek.Monday, 10);
        var booking = new CourtBooking
        {
            CourtId = court.Id,
            StartsAtUtc = startsAt,
            EndsAtUtc = startsAt.AddMinutes(90)
        };
        db.Users.Add(player);
        db.CourtBookings.Add(booking);
        db.PlayerPaymentMethods.Add(new PlayerPaymentMethod
        {
            UserId = player.Id,
            MercadoPagoCustomerId = "3464419562",
            MercadoPagoAccountEmail = "comprador@mercadopago.com",
            PaymentMethodId = "mercadopago_account",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var payment = new Payment
        {
            MatchId = Guid.NewGuid(),
            UserId = player.Id,
            Amount = 2500m,
            OwnerAmount = 2325m,
            AdminFeeAmount = 75m,
            ProcessingReserveAmount = 100m,
            Status = PaymentStatus.Due
        };
        var match = new PadelMatch
        {
            Id = payment.MatchId,
            CreatorId = player.Id,
            CourtId = court.Id,
            CourtBookingId = booking.Id,
            StartsAtUtc = booking.StartsAtUtc,
            EndsAtUtc = booking.EndsAtUtc,
            RequiredCategory = SkillCategory.Sexta,
            RequiredLevel = SkillLevel.Bajo,
            Status = MatchStatus.Completed,
            Players =
            {
                new MatchPlayer { UserId = player.Id, TeamNumber = 1 }
            },
            Payments = { payment }
        };
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var httpHandler = new FakeMercadoPagoHandler();
        var service = new MercadoPagoService(
            db,
            new HttpClient(httpHandler),
            Options.Create(new MercadoPagoOptions { SandboxPayerEmail = "buyer@testuser.com" }),
            new FakeNotifications());

        await service.CreatePreferenceAsync(player, match.Id, CancellationToken.None);

        Assert.Contains("\"email\":\"comprador@mercadopago.com\"", httpHandler.LastRequestBody);
        Assert.DoesNotContain("\"email\":\"registro@miapp.com\"", httpHandler.LastRequestBody);
        Assert.DoesNotContain("\"email\":\"buyer@testuser.com\"", httpHandler.LastRequestBody);
    }

    [Fact]
    public async Task MercadoPagoService_RequiresLinkedAccountInProduction()
    {
        await using var db = CreateDbContext();
        var court = await SeedApprovedClubAsync(db);
        var owner = await db.Users.SingleAsync(user => user.Email == "owner@test.local");
        owner.MercadoPagoAccessToken = "owner-token";
        db.MercadoPagoSettings.Add(new MercadoPagoSettings { Environment = MercadoPagoEnvironment.Production });
        var player = CreateUser("payer@test.local");
        var startsAt = NextWeekdayUtc(DayOfWeek.Monday, 10);
        var booking = new CourtBooking
        {
            CourtId = court.Id,
            StartsAtUtc = startsAt,
            EndsAtUtc = startsAt.AddMinutes(90)
        };
        db.Users.Add(player);
        db.CourtBookings.Add(booking);
        await db.SaveChangesAsync();

        var payment = new Payment
        {
            MatchId = Guid.NewGuid(),
            UserId = player.Id,
            Amount = 2500m,
            OwnerAmount = 2325m,
            AdminFeeAmount = 75m,
            ProcessingReserveAmount = 100m,
            Status = PaymentStatus.Due
        };
        var match = new PadelMatch
        {
            Id = payment.MatchId,
            CreatorId = player.Id,
            CourtId = court.Id,
            CourtBookingId = booking.Id,
            StartsAtUtc = booking.StartsAtUtc,
            EndsAtUtc = booking.EndsAtUtc,
            RequiredCategory = SkillCategory.Sexta,
            RequiredLevel = SkillLevel.Bajo,
            Status = MatchStatus.Completed,
            Players =
            {
                new MatchPlayer { UserId = player.Id, TeamNumber = 1 }
            },
            Payments = { payment }
        };
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var service = new MercadoPagoService(
            db,
            new HttpClient(new FakeMercadoPagoHandler()),
            Options.Create(new MercadoPagoOptions()),
            new FakeNotifications());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreatePreferenceAsync(player, match.Id, CancellationToken.None));

        Assert.Contains("vincular tu cuenta de Mercado Pago", ex.Message);
    }

    private static AppDbContext CreateDbContext()
    {
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
    }

    private static async Task<Court> SeedApprovedClubAsync(AppDbContext db)
    {
        var owner = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "owner@test.local",
            Email = "owner@test.local",
            EmailConfirmed = true,
            FullName = "Owner Test",
            MercadoPagoPublicKey = "owner-public-key"
        };

        var club = new Club
        {
            OwnerId = owner.Id,
            Name = "Club Test",
            Status = ClubStatus.Approved,
            PasswordHash = "test",
            Address = "Test 123",
            City = "Test",
            CourtCount = 1,
            FullMatchPrice = 10000m,
            Schedules =
            {
                new ClubSchedule { DayOfWeek = DayOfWeek.Monday, OpensAt = new TimeOnly(8, 0), ClosesAt = new TimeOnly(22, 0), SlotMinutes = 90 }
            },
            Courts =
            {
                new Court
                {
                    Name = "Cancha 1",
                    FloorType = "Cesped sintetico",
                    WallType = "Vidrio",
                    FullMatchPrice = 10000m,
                    Schedules =
                    {
                        new CourtSchedule { DayOfWeek = DayOfWeek.Monday, OpensAt = new TimeOnly(8, 0), ClosesAt = new TimeOnly(22, 0), SlotMinutes = 90 }
                    }
                }
            }
        };

        AddDailySchedules(club);
        db.Users.Add(owner);
        db.Clubs.Add(club);
        await db.SaveChangesAsync();

        return club.Courts.Single();
    }

    private static async Task<Court> AddCourtAsync(AppDbContext db, Guid clubId, string name)
    {
        var court = new Court
        {
            ClubId = clubId,
            Name = name,
            FloorType = "Cesped sintetico",
            WallType = "Vidrio",
            FullMatchPrice = 10000m,
            Schedules =
            {
                new CourtSchedule { DayOfWeek = DayOfWeek.Monday, OpensAt = new TimeOnly(8, 0), ClosesAt = new TimeOnly(22, 0), SlotMinutes = 90 }
            }
        };
        AddDailySchedules(court);
        db.Courts.Add(court);
        await db.SaveChangesAsync();
        return court;
    }

    private static void AddDailySchedules(Club club)
    {
        foreach (var dayOfWeek in Enum.GetValues<DayOfWeek>())
        {
            if (!club.Schedules.Any(schedule => schedule.DayOfWeek == dayOfWeek))
            {
                club.Schedules.Add(new ClubSchedule { DayOfWeek = dayOfWeek, OpensAt = new TimeOnly(8, 0), ClosesAt = new TimeOnly(22, 0), SlotMinutes = 90 });
            }

            foreach (var court in club.Courts.Where(court => !court.Schedules.Any(schedule => schedule.DayOfWeek == dayOfWeek)))
            {
                court.Schedules.Add(new CourtSchedule { DayOfWeek = dayOfWeek, OpensAt = new TimeOnly(8, 0), ClosesAt = new TimeOnly(22, 0), SlotMinutes = 90 });
            }
        }
    }

    private static void AddDailySchedules(Court court)
    {
        foreach (var dayOfWeek in Enum.GetValues<DayOfWeek>().Where(dayOfWeek => !court.Schedules.Any(schedule => schedule.DayOfWeek == dayOfWeek)))
        {
            court.Schedules.Add(new CourtSchedule { DayOfWeek = dayOfWeek, OpensAt = new TimeOnly(8, 0), ClosesAt = new TimeOnly(22, 0), SlotMinutes = 90 });
        }
    }

    private static ApplicationUser CreateUser(string email)
    {
        return new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = email,
            Category = SkillCategory.Sexta,
            Level = SkillLevel.Bajo
        };
    }

    private static void AddPlayerPaymentMethod(AppDbContext db, ApplicationUser user)
    {
        db.PlayerPaymentMethods.Add(new PlayerPaymentMethod
        {
            UserId = user.Id,
            CardToken = "card-token",
            PaymentMethodId = "visa",
            CardBrand = "Visa",
            LastFourDigits = "1234"
        });
        db.SaveChanges();
    }

    private static void AddLinkedMercadoPagoAccount(AppDbContext db, ApplicationUser user)
    {
        db.PlayerPaymentMethods.Add(new PlayerPaymentMethod
        {
            UserId = user.Id,
            MercadoPagoCustomerId = $"customer-{user.Id}",
            PaymentMethodId = "mercadopago_account",
            CardBrand = "Mercado Pago"
        });
        db.SaveChanges();
    }

    private static DateTime NextWeekdayUtc(DayOfWeek dayOfWeek, int hour)
    {
        var now = DateTime.UtcNow;
        var date = DateOnly.FromDateTime(now.Date).AddDays(1);
        var startsAt = date.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Utc);
        return startsAt > now ? startsAt : startsAt.AddDays(1);
    }

    private sealed class FakeNotifications : INotificationService
    {
        public List<Guid> CancelledMatches { get; } = [];
        public List<Guid> DuePayments { get; } = [];

        public Task NotifyEligibleMatchCreatedAsync(PadelMatch match, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task NotifyJoinRequestAsync(JoinRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task NotifyMatchFullAsync(PadelMatch match, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task NotifyMatchCancelledAsync(PadelMatch match, CancellationToken cancellationToken)
        {
            CancelledMatches.Add(match.Id);
            return Task.CompletedTask;
        }

        public Task NotifyPlayerLeftAsync(PadelMatch match, string userId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task NotifyPaymentUpdatedAsync(Payment payment, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task NotifyPaymentDueAsync(Payment payment, CancellationToken cancellationToken)
        {
            DuePayments.Add(payment.Id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMercadoPagoService(AppDbContext db) : IMercadoPagoService
    {
        public Task<PaymentPreferenceResponse> CreatePreferenceAsync(ApplicationUser user, Guid matchId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PaymentPreferenceResponse> ReservePlayerPaymentAsync(ApplicationUser user, Guid matchId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new PaymentPreferenceResponse(
                Guid.NewGuid(),
                $"fake-{Guid.NewGuid():N}",
                string.Empty,
                PaymentStatus.Reserved,
                0m,
                0m,
                0m,
                0m));
        }

        public Task UpdatePaymentAsync(string providerPaymentId, PaymentStatus status, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task SyncPaymentFromProviderAsync(Guid paymentId, string providerPaymentId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public async Task CancelPlayerPaymentAsync(Guid paymentId, CancellationToken cancellationToken)
        {
            var payment = await db.Payments.SingleAsync(payment => payment.Id == paymentId, cancellationToken);
            payment.Status = PaymentStatus.Cancelled;
            await db.SaveChangesAsync(cancellationToken);
        }

        public Task<int> CompleteFinishedMatchesAsync(DateTime nowUtc, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeMercadoPagoHandler : HttpMessageHandler
    {
        public string LastRequestBody { get; private set; } = string.Empty;
        public string LastIdempotencyKey { get; private set; } = string.Empty;
        public string LastRequestPath { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestPath = request.RequestUri?.AbsolutePath ?? string.Empty;
            LastRequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            LastIdempotencyKey = request.Headers.TryGetValues("X-Idempotency-Key", out var values)
                ? values.FirstOrDefault() ?? string.Empty
                : string.Empty;

            if (request.RequestUri?.AbsolutePath.Contains("/v1/payments/") == true)
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "id": 123,
                          "status": "approved"
                        }
                        """,
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "id": "pref-test",
                      "init_point": "https://checkout.prod",
                      "sandbox_init_point": "https://checkout.sandbox"
                    }
                    """,
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
