// Functional harness for MrShooferAPIClient.CancelTicketAsync
// Run: dotnet run --project tools/CancelClientTest
#:sdk Microsoft.NET.Sdk
#:property OutputType=Exe
#:property TargetFramework=net8.0
#:property ImplicitUsings=enable
#:property Nullable=enable
#:package Microsoft.Extensions.Configuration.Json@8.0.0
#:property RestoreIgnoreFailedSources=true

using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

var cfgPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "appsettings.Development.json"));
if (!File.Exists(cfgPath))
  cfgPath = Path.GetFullPath("appsettings.Development.json");

using var cfgDoc = JsonDocument.Parse(await File.ReadAllTextAsync(cfgPath));
var mr = cfgDoc.RootElement.GetProperty("MrShoofer");
var baseUrl = mr.GetProperty("ApiBaseUrl").GetString()!.TrimEnd('/');
var token = mr.GetProperty("SellerToken").GetString()!;

using var client = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(60) };
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

int pass = 0, fail = 0;
void Check(string name, bool ok, string detail = "")
{
  if (ok) { pass++; Console.WriteLine($"  PASS  {name}" + (detail == "" ? "" : $" — {detail}")); }
  else { fail++; Console.WriteLine($"  FAIL  {name}" + (detail == "" ? "" : $" — {detail}")); }
}

Console.WriteLine("=== C# CancelTicketAsync client harness ===");
Console.WriteLine($"ORS: {baseUrl}\n");

// Mirror CancelTicketAsync
async Task<(bool ok, decimal refund, string? err)> CancelAsync(string? ticketCode, string? reason)
{
  if (string.IsNullOrWhiteSpace(ticketCode))
    return (false, 0, "کد بلیط نامعتبر است");

  var qs = $"ticketcode={Uri.EscapeDataString(ticketCode.Trim())}";
  if (!string.IsNullOrWhiteSpace(reason))
    qs += $"&reason={Uri.EscapeDataString(reason.Trim())}";

  var resp = await client.PostAsync($"/Tickets/cancelTicket?{qs}", null);
  var body = await resp.Content.ReadAsStringAsync();
  if (!resp.IsSuccessStatusCode)
  {
    var msg = body.Trim().Trim('"');
    if (msg.Contains("ALREADY CANCELED", StringComparison.OrdinalIgnoreCase))
      msg = "این بلیط قبلاً لغو شده است";
    else if (msg.Contains("NO SUCH A TICKET", StringComparison.OrdinalIgnoreCase))
      msg = "بلیط در سامانه ORS یافت نشد";
    return (false, 0, msg);
  }

  decimal refund = 0;
  try
  {
    var node = JsonNode.Parse(body);
    var n = node?["rerfund"] ?? node?["refund"];
    if (n != null) decimal.TryParse(n.ToString(), out refund);
  }
  catch { /* ignore */ }
  return (true, refund, null);
}

var r1 = await CancelAsync("", "x");
Check("empty code rejected", !r1.ok && r1.err!.Contains("نامعتبر"));

var r2 = await CancelAsync("NO-SUCH-TICKET-XYZ", "تست");
Check("missing ticket Persian error", !r2.ok && r2.err!.Contains("یافت نشد"), r2.err);

// Create disposable ticket then cancel via same client contract
var start = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd");
var end = DateTime.Today.AddDays(7).ToString("yyyy-MM-dd");
// Tehran city_id often ~1xx — pull first direction with trips
var dirsJson = await client.GetStringAsync("/Directions/getAvailableDirections");
using var dirsDoc = JsonDocument.Parse(dirsJson);
string? tripCode = null;
foreach (var d in dirsDoc.RootElement.EnumerateArray())
{
  var o = d.GetProperty("origin").GetProperty("city_id").GetInt32();
  var dest = d.GetProperty("destination").GetProperty("city_id").GetInt32();
  var on = d.GetProperty("origin").GetProperty("city_name").GetString() ?? "";
  if (!on.Contains("تهران")) continue;
  var tripsRaw = await client.GetStringAsync($"/Trips/GetPlanedTripsbyCityID/{start}/{end}/{o}/{dest}");
  if (string.IsNullOrWhiteSpace(tripsRaw) || tripsRaw == "[]" || tripsRaw == "null") continue;
  using var tripsDoc = JsonDocument.Parse(tripsRaw);
  if (tripsDoc.RootElement.ValueKind != JsonValueKind.Array || tripsDoc.RootElement.GetArrayLength() == 0) continue;
  tripCode = tripsDoc.RootElement[0].GetProperty("tripPlanCode").GetString();
  if (!string.IsNullOrWhiteSpace(tripCode)) break;
}

Check("found trip for client harness", !string.IsNullOrWhiteSpace(tripCode), tripCode ?? "");
if (!string.IsNullOrWhiteSpace(tripCode))
{
  var tempBody = JsonSerializer.Serialize(new { tripCode, isPrivate = true, seatnumber = (int?)null });
  using var tempContent = new StringContent(tempBody, System.Text.Encoding.UTF8, "application/json");
  var tempResp = await client.PostAsync("/Tickets/reserverTemporarily", tempContent);
  var tempRaw = await tempResp.Content.ReadAsStringAsync();
  var tempNode = JsonNode.Parse(tempRaw);
  var reserveCode = tempNode?["ticketCode"]?.ToString();
  Check("temp reserve via client path", tempResp.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(reserveCode), reserveCode ?? tempRaw[..Math.Min(120, tempRaw.Length)]);

  if (!string.IsNullOrWhiteSpace(reserveCode))
  {
    var confBody = JsonSerializer.Serialize(new
    {
      reservationCode = reserveCode,
      passengerFirstName = "تست",
      passengerLastName = "کلاینت",
      passengerNumberPhone = "09121111111",
      passengerNationalCode = "0011111111"
    });
    using var confContent = new StringContent(confBody, System.Text.Encoding.UTF8, "application/json");
    var confResp = await client.PostAsync("/Tickets/confirmReserve", confContent);
    var confRaw = await confResp.Content.ReadAsStringAsync();
    var confNode = JsonNode.Parse(confRaw);
    var ticket = confNode?["ticketCode"]?.ToString();
    Check("confirm via client path", confResp.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(ticket), ticket ?? confRaw[..Math.Min(120, confRaw.Length)]);

    if (!string.IsNullOrWhiteSpace(ticket))
    {
      var cancel = await CancelAsync(ticket, "تست لغو از کلاینت C#");
      Check("CancelTicketAsync success + refund", cancel.ok && cancel.refund >= 0, $"refund={cancel.refund}");
      var again = await CancelAsync(ticket, "again");
      Check("CancelTicketAsync double-cancel mapped", !again.ok && again.err!.Contains("قبلاً"), again.err);
    }
  }
}

Console.WriteLine($"\n=== Summary ===\nPassed: {pass}  Failed: {fail}");
return fail == 0 ? 0 : 1;
