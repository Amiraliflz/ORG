using IPE.SmsIrClient;
using IPE.SmsIrClient.Models.Requests;
using IPE.SmsIrClient.Models.Results;
using Kavenegar;
using Kavenegar.Core.Models.Enums;

namespace Application.Services
{
  public class CustomerServiceSmsSender
  {
    private readonly SmsIr smsIr;

    public CustomerServiceSmsSender(IConfiguration configuration)
    {
      this.smsIr = new SmsIr(configuration["smsirapikey"]);
    }


    public async Task SendCustomerTicket_issued(string firstname, string lastname, string reference, string link, string numberphone)
    {
      // SMS provider max length for template parameters is 25 characters (provider error shown)
      static string Truncate(string? value, int maxLength = 25)
      {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.Length <= maxLength) return value;
        // Log truncation for debugging (console used to avoid changing DI)
        Console.WriteLine($"[CustomerServiceSmsSender] Truncating SMS parameter from {value.Length} to {maxLength} chars. Original: {value}");
        return value.Substring(0, maxLength);
      }

      int templateId = 782252; // use the actual template id used previously

      // Ensure parameters do not exceed provider limits
      VerifySendParameter[] verifySendParameters = {
           new VerifySendParameter("FIRSTNAME", Truncate(firstname)),
           new VerifySendParameter("LASTNAME", Truncate(lastname)),
           new VerifySendParameter("TRIP", Truncate(link)),
           new VerifySendParameter("REFERENCE", Truncate(reference)),

        };

      try
      {
        var response = await smsIr.VerifySendAsync(numberphone, templateId, verifySendParameters);

        // Optionally inspect response for non-success
        if (response == null)
        {
          Console.WriteLine("[CustomerServiceSmsSender] SMS provider returned null response.");
        }

      }
      catch (IPE.SmsIrClient.Exceptions.LogicalException lex)
      {
        // Parameter length errors and other logical exceptions from SmsIr
        Console.WriteLine($"[CustomerServiceSmsSender] SmsIr LogicalException: {lex.Message}");
        throw; // rethrow so caller can handle/log if needed
      }
      catch (Exception ex)
      {
        Console.WriteLine($"[CustomerServiceSmsSender] Exception sending SMS: {ex.Message}");
        // Do not fail user flow on SMS failure - swallow or rethrow based on your preference
        // For now, swallow after logging
      }

    }
  }
}
