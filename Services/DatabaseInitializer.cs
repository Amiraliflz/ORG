using Application.Data;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
  public static class DatabaseInitializer
  {
    public static async Task EnsureGuestAgencyExists(AppDbContext context, IConfiguration configuration)
    {
      var sellerToken = configuration["MrShoofer:SellerToken"];
      if (string.IsNullOrWhiteSpace(sellerToken)) return;

      var saleAgency = await context.Agencies
        .FirstOrDefaultAsync(a => a.IdentityUser != null && a.IdentityUser.UserName == "Sale.mrshoofer");

      if (saleAgency == null)
      {
        Console.WriteLine("WARNING: Sale.mrshoofer agency not found. Guest bookings will have no default seller agency.");
        return;
      }

      if (saleAgency.ORSAPI_token != sellerToken)
      {
        saleAgency.ORSAPI_token = sellerToken;
        await context.SaveChangesAsync();
      }
    }
  }
}
