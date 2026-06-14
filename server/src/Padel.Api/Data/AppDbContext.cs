using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Padel.Api.Domain;

namespace Padel.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<UserFollow> UserFollows => Set<UserFollow>();
    public DbSet<Club> Clubs => Set<Club>();
    public DbSet<Court> Courts => Set<Court>();
    public DbSet<ClubSchedule> ClubSchedules => Set<ClubSchedule>();
    public DbSet<CourtSchedule> CourtSchedules => Set<CourtSchedule>();
    public DbSet<CourtBooking> CourtBookings => Set<CourtBooking>();
    public DbSet<PadelMatch> Matches => Set<PadelMatch>();
    public DbSet<MatchPlayer> MatchPlayers => Set<MatchPlayer>();
    public DbSet<JoinRequest> JoinRequests => Set<JoinRequest>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PlayerPaymentMethod> PlayerPaymentMethods => Set<PlayerPaymentMethod>();
    public DbSet<MercadoPagoSettings> MercadoPagoSettings => Set<MercadoPagoSettings>();
    public DbSet<MercadoPagoOAuthState> MercadoPagoOAuthStates => Set<MercadoPagoOAuthState>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<UserFollow>(entity =>
        {
            entity.HasKey(x => new { x.FollowerId, x.FollowedId });
            entity.HasOne(x => x.Follower).WithMany().HasForeignKey(x => x.FollowerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Followed).WithMany().HasForeignKey(x => x.FollowedId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Club>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(160);
            entity.Property(x => x.FullMatchPrice).HasPrecision(10, 2);
            entity.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Court>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(80);
            entity.Property(x => x.FloorType).HasMaxLength(80);
            entity.Property(x => x.WallType).HasMaxLength(80);
            entity.Property(x => x.FullMatchPrice).HasPrecision(10, 2);
            entity.HasIndex(x => new { x.ClubId, x.Name }).IsUnique();
        });

        builder.Entity<ClubSchedule>(entity =>
        {
            entity.HasIndex(x => new { x.ClubId, x.DayOfWeek, x.OpensAt, x.ClosesAt });
        });

        builder.Entity<CourtSchedule>(entity =>
        {
            entity.HasIndex(x => new { x.CourtId, x.DayOfWeek, x.OpensAt, x.ClosesAt });
            entity.HasOne(x => x.Court).WithMany(x => x.Schedules).HasForeignKey(x => x.CourtId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CourtBooking>(entity =>
        {
            entity.HasIndex(x => new { x.CourtId, x.StartsAtUtc, x.EndsAtUtc })
                .IsUnique()
                .HasFilter("[IsCancelled] = 0");
            entity.HasOne(x => x.Court).WithMany().HasForeignKey(x => x.CourtId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PadelMatch>(entity =>
        {
            entity.HasOne(x => x.Creator).WithMany().HasForeignKey(x => x.CreatorId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Court).WithMany().HasForeignKey(x => x.CourtId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CourtBooking)
                .WithOne(x => x.Match)
                .HasForeignKey<PadelMatch>(x => x.CourtBookingId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<MatchPlayer>(entity =>
        {
            entity.HasIndex(x => new { x.MatchId, x.UserId }).IsUnique();
            entity.HasOne(x => x.Match).WithMany(x => x.Players).HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<JoinRequest>(entity =>
        {
            entity.HasIndex(x => new { x.MatchId, x.UserId }).IsUnique();
            entity.HasOne(x => x.Match).WithMany(x => x.JoinRequests).HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Payment>(entity =>
        {
            entity.Property(x => x.Amount).HasPrecision(10, 2);
            entity.Property(x => x.OwnerAmount).HasPrecision(10, 2);
            entity.Property(x => x.AdminFeeAmount).HasPrecision(10, 2);
            entity.Property(x => x.ProcessingReserveAmount).HasPrecision(10, 2);
            entity.HasIndex(x => new { x.MatchId, x.UserId });
            entity.HasOne(x => x.Match).WithMany(x => x.Payments).HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PlayerPaymentMethod>(entity =>
        {
            entity.Property(x => x.PaymentMethodId).HasMaxLength(80);
            entity.Property(x => x.CardBrand).HasMaxLength(80);
            entity.Property(x => x.LastFourDigits).HasMaxLength(8);
            entity.HasIndex(x => x.UserId).IsUnique();
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MercadoPagoSettings>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
        });

        builder.Entity<MercadoPagoOAuthState>(entity =>
        {
            entity.HasKey(x => x.State);
            entity.Property(x => x.State).HasMaxLength(120);
            entity.Property(x => x.Purpose).HasMaxLength(40);
            entity.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Notification>(entity =>
        {
            entity.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAtUtc });
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
