using System.Security.Cryptography;
using System.Text;

namespace Application.Services.Ops
{
    public interface IOpsMobileTokenService
    {
        string Issue(string userId, string userName);
        bool TryValidate(string? token, out string userId, out string userName);
    }

    /// <summary>
    /// Stateless HMAC token for the Ops Android APK (survives process restart).
    /// </summary>
    public class OpsMobileTokenService : IOpsMobileTokenService
    {
        private readonly byte[] _key;

        public OpsMobileTokenService(IConfiguration config)
        {
            var secret = config["Ops:MobileTokenSecret"]
                ?? config["PaymentServer:SharedKey"]
                ?? "ops-dev-mobile-token-secret-change-me";
            _key = Encoding.UTF8.GetBytes(secret);
        }

        public string Issue(string userId, string userName)
        {
            var exp = DateTimeOffset.UtcNow.AddDays(365).ToUnixTimeSeconds();
            var payload = $"{userId}|{userName}|{exp}";
            var sig = Sign(payload);
            return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{payload}|{sig}"));
        }

        public bool TryValidate(string? token, out string userId, out string userName)
        {
            userId = "";
            userName = "";
            if (string.IsNullOrWhiteSpace(token)) return false;

            try
            {
                var raw = Encoding.UTF8.GetString(Convert.FromBase64String(token.Trim()));
                var parts = raw.Split('|');
                if (parts.Length != 4) return false;

                var payload = $"{parts[0]}|{parts[1]}|{parts[2]}";
                if (!FixedTimeEquals(Sign(payload), parts[3])) return false;
                if (!long.TryParse(parts[2], out var exp)) return false;
                if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp) return false;

                userId = parts[0];
                userName = parts[1];
                return !string.IsNullOrEmpty(userId);
            }
            catch
            {
                return false;
            }
        }

        private string Sign(string payload)
        {
            using var hmac = new HMACSHA256(_key);
            return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            var ba = Encoding.UTF8.GetBytes(a);
            var bb = Encoding.UTF8.GetBytes(b);
            return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
        }
    }
}
