using IPE.SmsIrClient;
using IPE.SmsIrClient.Models.Requests;
using System.Security.Cryptography;
using System.Text;

namespace Application.Services.Auth
{
    public class SmsIrOtp : IOtpLogin
    {
        private readonly SmsIr _smsIr;
        private readonly int _templateId;
        private readonly ILogger<SmsIrOtp> _logger;

        public SmsIrOtp(IConfiguration configuration, ILogger<SmsIrOtp> logger)
        {
            _smsIr = new SmsIr(configuration["smsirapikey"] ?? string.Empty);
            _templateId = configuration.GetValue<int>("SmsIr:OtpTemplateId");
            _logger = logger;
        }

        public async Task<string> SendCode(string numberphone)
        {
            var code = GenerateOtp();

            try
            {
                var parameters = new[]
                {
                    new VerifySendParameter("CODE", code)
                };

                var response = await _smsIr.VerifySendAsync(numberphone, _templateId, parameters);
                _logger.LogInformation("OTP sent via SmsIr to {Phone}. Status={Status}", numberphone, response?.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send OTP via SmsIr to {Phone}", numberphone);
                // Return the code anyway so the flow is not broken during development
            }

            return code;
        }

        private static string GenerateOtp(int length = 5)
        {
            using var random = RandomNumberGenerator.Create();
            var bytes = new byte[length];
            random.GetBytes(bytes);
            var sb = new StringBuilder(length);
            for (int i = 0; i < length; i++)
                sb.Append((bytes[i] & 0xF) % 10);
            return sb.ToString();
        }
    }
}
