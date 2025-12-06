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

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
      base.OnModelCreating(modelBuilder);
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
