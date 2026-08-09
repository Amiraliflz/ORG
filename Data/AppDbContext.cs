using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Application.Models;

namespace Application.Data
{
  public class AppDbContext : IdentityDbContext<IdentityUser>
  {
    public DbSet<Agency> Agencies { get; set; }
    public DbSet<Ticket> Tickets { get; set; }
    public DbSet<AdminUser> AdminUsers { get; set; }
    public DbSet<AgencyBalanceCharge> AgencyBalanceCharges { get; set; }
    public DbSet<ChargePaymentRequest> ChargePaymentRequests { get; set; }

    public DbSet<ContactUsMessage> ContactMessages { get; set; }
    public DbSet<CustomerProfile> CustomerProfiles { get; set; }
    public DbSet<DiscountCode> DiscountCodes { get; set; }
    public DbSet<DiscountCodeUsage> DiscountCodeUsages { get; set; }

    public DbSet<CityCoordinate> CityCoordinates { get; set; }
    public DbSet<RouteTravelTime> RouteTravelTimes { get; set; }
    public DbSet<TravelTimeSyncState> TravelTimeSyncStates { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
      base.OnModelCreating(modelBuilder);

      modelBuilder.Entity<CityCoordinate>(e =>
      {
        e.HasIndex(x => x.CityId).IsUnique();
        e.HasIndex(x => x.NameFa);
      });

      modelBuilder.Entity<RouteTravelTime>(e =>
      {
        e.HasIndex(x => new { x.OriginCityId, x.DestinationCityId }).IsUnique();
        e.HasIndex(x => new { x.OriginNameFa, x.DestinationNameFa });
      });
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
      base.OnConfiguring(optionsBuilder);
    }

    static AppDbContext()
    {
      // Configure Npgsql to use timestamp without time zone for DateTime
      AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }
  }
}
