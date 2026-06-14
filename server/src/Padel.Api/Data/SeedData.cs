using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Padel.Api.Domain;

namespace Padel.Api.Data;

public static class SeedData
{
    public static async Task InitializeAsync(
        IServiceProvider services,
        IConfiguration configuration,
        IHostEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var role in new[] { "Admin", "Player", "ClubOwner" })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        var adminEmail = configuration["Seed:AdminEmail"] ?? (environment.IsDevelopment() ? "admin@padel.local" : null);
        var adminPassword = configuration["Seed:AdminPassword"] ?? (environment.IsDevelopment() ? "Admin123!" : null);
        if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
        {
            var admin = await userManager.FindByEmailAsync(adminEmail);
            if (admin is null)
            {
                admin = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    EmailConfirmed = true,
                    FullName = "Administrador Padel",
                    Category = SkillCategory.Primera,
                    Level = SkillLevel.Alto
                };

                await userManager.CreateAsync(admin, adminPassword);
                await userManager.AddToRoleAsync(admin, "Admin");
            }
        }

        var seedDemoData = configuration.GetValue<bool?>("Seed:DemoData") ?? environment.IsDevelopment();
        if (seedDemoData && !await db.Clubs.AnyAsync(cancellationToken))
        {
            var demoOwner = await userManager.FindByEmailAsync(adminEmail ?? "admin@padel.local");
            if (demoOwner is null)
            {
                return;
            }

            var club = new Club
            {
                OwnerId = demoOwner.Id,
                Name = "Club Demo",
                PasswordHash = "seed",
                Status = ClubStatus.Approved,
                Address = "Av. Principal 123",
                City = "Buenos Aires",
                CourtCount = 2,
                FullMatchPrice = 12000m,
                Schedules =
                {
                    new ClubSchedule { DayOfWeek = DayOfWeek.Monday, OpensAt = new TimeOnly(8, 0), ClosesAt = new TimeOnly(23, 0), SlotMinutes = 90 },
                    new ClubSchedule { DayOfWeek = DayOfWeek.Tuesday, OpensAt = new TimeOnly(8, 0), ClosesAt = new TimeOnly(23, 0), SlotMinutes = 90 },
                    new ClubSchedule { DayOfWeek = DayOfWeek.Wednesday, OpensAt = new TimeOnly(8, 0), ClosesAt = new TimeOnly(23, 0), SlotMinutes = 90 },
                    new ClubSchedule { DayOfWeek = DayOfWeek.Thursday, OpensAt = new TimeOnly(8, 0), ClosesAt = new TimeOnly(23, 0), SlotMinutes = 90 },
                    new ClubSchedule { DayOfWeek = DayOfWeek.Friday, OpensAt = new TimeOnly(8, 0), ClosesAt = new TimeOnly(23, 0), SlotMinutes = 90 },
                    new ClubSchedule { DayOfWeek = DayOfWeek.Saturday, OpensAt = new TimeOnly(9, 0), ClosesAt = new TimeOnly(22, 0), SlotMinutes = 90 },
                    new ClubSchedule { DayOfWeek = DayOfWeek.Sunday, OpensAt = new TimeOnly(9, 0), ClosesAt = new TimeOnly(22, 0), SlotMinutes = 90 }
                },
                Courts =
                {
                    new Court
                    {
                        Name = "Cancha 1",
                        IsCovered = true,
                        FloorType = "Cesped sintetico",
                        WallType = "Vidrio",
                        FullMatchPrice = 12000m,
                        Schedules =
                        {
                            new CourtSchedule { DayOfWeek = DayOfWeek.Monday, OpensAt = new TimeOnly(8, 0), ClosesAt = new TimeOnly(23, 0), SlotMinutes = 90 },
                            new CourtSchedule { DayOfWeek = DayOfWeek.Tuesday, OpensAt = new TimeOnly(8, 0), ClosesAt = new TimeOnly(23, 0), SlotMinutes = 90 },
                            new CourtSchedule { DayOfWeek = DayOfWeek.Wednesday, OpensAt = new TimeOnly(8, 0), ClosesAt = new TimeOnly(23, 0), SlotMinutes = 90 },
                            new CourtSchedule { DayOfWeek = DayOfWeek.Thursday, OpensAt = new TimeOnly(8, 0), ClosesAt = new TimeOnly(23, 0), SlotMinutes = 90 },
                            new CourtSchedule { DayOfWeek = DayOfWeek.Friday, OpensAt = new TimeOnly(8, 0), ClosesAt = new TimeOnly(23, 0), SlotMinutes = 90 },
                            new CourtSchedule { DayOfWeek = DayOfWeek.Saturday, OpensAt = new TimeOnly(9, 0), ClosesAt = new TimeOnly(22, 0), SlotMinutes = 90 },
                            new CourtSchedule { DayOfWeek = DayOfWeek.Sunday, OpensAt = new TimeOnly(9, 0), ClosesAt = new TimeOnly(22, 0), SlotMinutes = 90 }
                        }
                    },
                    new Court
                    {
                        Name = "Cancha 2",
                        IsCovered = false,
                        FloorType = "Cesped sintetico",
                        WallType = "Muro",
                        FullMatchPrice = 10000m,
                        Schedules =
                        {
                            new CourtSchedule { DayOfWeek = DayOfWeek.Monday, OpensAt = new TimeOnly(8, 0), ClosesAt = new TimeOnly(23, 0), SlotMinutes = 90 },
                            new CourtSchedule { DayOfWeek = DayOfWeek.Tuesday, OpensAt = new TimeOnly(8, 0), ClosesAt = new TimeOnly(23, 0), SlotMinutes = 90 },
                            new CourtSchedule { DayOfWeek = DayOfWeek.Wednesday, OpensAt = new TimeOnly(8, 0), ClosesAt = new TimeOnly(23, 0), SlotMinutes = 90 },
                            new CourtSchedule { DayOfWeek = DayOfWeek.Thursday, OpensAt = new TimeOnly(8, 0), ClosesAt = new TimeOnly(23, 0), SlotMinutes = 90 },
                            new CourtSchedule { DayOfWeek = DayOfWeek.Friday, OpensAt = new TimeOnly(8, 0), ClosesAt = new TimeOnly(23, 0), SlotMinutes = 90 },
                            new CourtSchedule { DayOfWeek = DayOfWeek.Saturday, OpensAt = new TimeOnly(9, 0), ClosesAt = new TimeOnly(22, 0), SlotMinutes = 90 },
                            new CourtSchedule { DayOfWeek = DayOfWeek.Sunday, OpensAt = new TimeOnly(9, 0), ClosesAt = new TimeOnly(22, 0), SlotMinutes = 90 }
                        }
                    }
                }
            };

            db.Clubs.Add(club);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
