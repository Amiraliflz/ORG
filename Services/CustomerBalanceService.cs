using Application.Data;
using Application.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    public class CustomerBalanceService
    {
        private readonly AppDbContext _context;

        public CustomerBalanceService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<decimal> GetBalance(string userId)
        {
            var profile = await _context.CustomerProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            return profile?.Balance ?? 0m;
        }

        public async Task SetBalance(string userId, decimal amount)
        {
            var profile = await _context.CustomerProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            if (profile == null)
            {
                profile = new CustomerProfile { UserId = userId, CreatedAt = DateTime.Now };
                _context.CustomerProfiles.Add(profile);
            }
            profile.Balance = amount;
            await _context.SaveChangesAsync();
        }

        public async Task<decimal> AddBalance(string userId, decimal amount)
        {
            var profile = await _context.CustomerProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            if (profile == null)
            {
                profile = new CustomerProfile { UserId = userId, CreatedAt = DateTime.Now };
                _context.CustomerProfiles.Add(profile);
            }
            profile.Balance += amount;
            await _context.SaveChangesAsync();
            return profile.Balance;
        }

        public async Task<(bool success, decimal newBalance)> DeductBalance(string userId, decimal amount)
        {
            var profile = await _context.CustomerProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            if (profile == null || profile.Balance < amount)
                return (false, profile?.Balance ?? 0m);

            profile.Balance -= amount;
            await _context.SaveChangesAsync();
            return (true, profile.Balance);
        }
    }
}
