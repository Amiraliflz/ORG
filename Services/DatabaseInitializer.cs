using Application.Data;
using Application.Models;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
  public static class DatabaseInitializer
  {
    public static async Task EnsureGuestAgencyExists(AppDbContext context)
    {
      // Check if guest agency already exists
      var guestAgency = await context.Agencies
        .FirstOrDefaultAsync(a => a.IdentityUser == null && a.Name.Contains("مهمان"));

      if (guestAgency == null)
      {
        // Create guest agency
        var newGuestAgency = new Agency
        {
          Name = "مستر شوفر - مهمان",
          PhoneNumber = "02100000000",
          Address = "تهران",
          AdminMobile = "09900000000",
          DateJoined = DateTime.Now,
          ORSAPI_token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1laWRlbnRpZmllciI6IjU1NCIsImp0aSI6Ijk1ZTI1NJYzLTkzM2EtNGY1ZS04ZTdiLTMwNGQ0Yjg3M2Q3NiIsImV4cCI6MTkyMjc3ODkwOCwiaXNzIjoibXJzaG9vZmVyLmlyIiwiYXVkIjoibXJzaG9vZmVyLmlyIn0.2r5WoGmqb5Ra_6epV5jR3Y0RlHs5bcwE0li0wo1ricE",
          Commission = 0,
          IdentityUser = null
        };

        context.Agencies.Add(newGuestAgency);
        await context.SaveChangesAsync();
      }
    }
  }
}
