using Application.ViewModels;
using System.Security.Policy;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Application.Services.MrShooferORS
{
  public class MrShooferAPIClient
  {
    string _apikey;
    readonly HttpClient _client;
    readonly string _sellerapikey;
    
    // JSON serializer options with case-insensitive property matching
    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true
    };

    // Shared client for the static login method — avoids socket exhaustion from new HttpClient() per call
    private static readonly HttpClient _loginClient = new HttpClient
    {
      BaseAddress = new Uri("https://ors.shoofer.taxi"),
      Timeout = TimeSpan.FromSeconds(30)
    };

    public MrShooferAPIClient(HttpClient client)
    {
      _client = client;
    }


    public void SetSellerApiKey(string apikey)
    {
      this._apikey = apikey;
      _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", this._apikey);

    }


    public static async Task<string> GetSellerApiKey_LoginAsync(string username, string password)
    {

      var result = await _loginClient.GetAsync($"/Account/Login?adminnumberphone={username}&password={password}");
      var node = JsonNode.Parse(await result.Content.ReadAsStringAsync());

      return node["token"].ToString();
    }


    public async Task<string> GetAccountBalance()
    {
      var result = await _client.GetAsync("/Account/getAccountBalance");


      var node = JsonNode.Parse(await result.Content.ReadAsStringAsync());

      return node["accountBalance_tomans"].ToString();

    }


    public async Task<IList<SearchedTrip>> SearchTrips(DateTime startspan, DateTime endspan, int originCityId, int destinationCityid, int? originterminalId = null, int? destinationterminalid = null)
    {
      string searchurl = $"/Trips/GetPlanedTripsbyCityID/{startspan:yyyy-MM-dd}/{endspan:yyyy-MM-dd}/{originCityId}/{destinationCityid}";


      if (originterminalId != null)
      {
        searchurl += $"/{originterminalId}";
      }
      if (destinationterminalid != null)
      {
        searchurl += $"/{destinationterminalid}";
      }


      var response = await _client.GetAsync(searchurl);
      response.EnsureSuccessStatusCode();
      
      var json = await response.Content.ReadAsStringAsync();
      var searchedtrips = JsonSerializer.Deserialize<List<SearchedTrip>>(json, _jsonOptions);

      return searchedtrips;
    }

    public async Task<SearchedTrip> GetTripInfo(string tripcode)
    {

      string searchurl = $"/Trips/getTripinfo?tripcode={tripcode}";
      
      var response = await _client.GetAsync(searchurl);
      response.EnsureSuccessStatusCode();
      
      var json = await response.Content.ReadAsStringAsync();
      var result = JsonSerializer.Deserialize<SearchedTrip>(json, _jsonOptions);

      if (result == null)
      {
        throw new Exception("Trip not found");
      }


      return result;
    }


    /// <summary>
    /// Reserves temporarirly the ticket for trip
    /// </summary>
    /// <param name="ticket">ticket needs for temporarirly reserved</param>
    /// <returns>Temporarirly reservatoin code</returns>
    public async Task<string> ReserveTicketTemporarirly(TicketTempReserveRequestModel ticket)
    {
      var response = await _client.PostAsJsonAsync<TicketTempReserveRequestModel>("/Tickets/reserverTemporarily", ticket);

      var jsonresult = await response.Content.ReadAsStringAsync();

      if (!response.IsSuccessStatusCode)
      {
        // Try to extract error message from API response
        try
        {
          var errorNode = JsonNode.Parse(jsonresult);
          var errorMessage = errorNode?["error"]?.ToString() 
                          ?? errorNode?["message"]?.ToString() 
                          ?? errorNode?["Message"]?.ToString()
                          ?? jsonresult;
          throw new Exception($"MrShoofer API Error ({(int)response.StatusCode}): {errorMessage}");
        }
        catch (JsonException)
        {
          throw new Exception($"MrShoofer API Error ({(int)response.StatusCode}): {jsonresult}");
        }
      }

      var node = JsonNode.Parse(jsonresult);

      if (node?["ticketCode"] == null)
      {
        throw new Exception($"MrShoofer API returned unexpected response: {jsonresult}");
      }

      return node["ticketCode"].ToString();
    }

    public async Task<TicketConfirmationResponse> ConfirmReserve(ConfirmReserveRequestModel confirmreservemodel)
    {
      var response = await _client.PostAsJsonAsync<ConfirmReserveRequestModel>("/Tickets/confirmReserve", confirmreservemodel);

      // When error happend
      if ((int)response.StatusCode != 200)
      {
        var jsonresult = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        throw new Exception(jsonresult["error"].ToString());
      }


      var jsonresponse = JsonNode.Parse(await response.Content.ReadAsStringAsync());

      var confirmationmodel = JsonSerializer.Deserialize<TicketConfirmationResponse>(jsonresponse);

      return confirmationmodel;
    }


    public async Task<string> RegisterOTA(RegisterOTADTO registerOTADTO)
    {
      string url = "/OTAManagement/RegisterNewOTA";



      var result = await _client.PostAsJsonAsync(url, registerOTADTO);
      if (!result.IsSuccessStatusCode)
      {
        throw new Exception();
      }

      return await result.Content.ReadAsStringAsync();
    }

    //Get available OTA directions
    public record AvaiableDirection(string Cityone, string Citytwo, int? CityoneId, int? CitytwoId);

    private static string? ExtractString(JsonNode? node)
    {
      if (node == null) return null;

      if (node is JsonValue jv && jv.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
      {
        return s;
      }

      if (node is JsonObject jobj)
      {
        // Prioritized child property names commonly used for city labels
        var candidateChildNames = new[]
        {
          "city_name","cityName","name","title","label","fa","persian","caption","display"
        };
        foreach (var childName in candidateChildNames)
        {
          if (jobj.TryGetPropertyValue(childName, out var child) && child is JsonValue cjv && cjv.TryGetValue<string>(out var cs) && !string.IsNullOrWhiteSpace(cs))
          {
            return cs;
          }
        }
        // Fallback: scan for first string value in object
        foreach (var kv in jobj)
        {
          var inner = ExtractString(kv.Value);
          if (!string.IsNullOrWhiteSpace(inner)) return inner;
        }
        return null;
      }

      if (node is JsonArray arr)
      {
        foreach (var el in arr)
        {
          var inner = ExtractString(el);
          if (!string.IsNullOrWhiteSpace(inner)) return inner;
        }
      }

      // As a last resort
      var asStr = node.ToString();
      return string.IsNullOrWhiteSpace(asStr) ? null : asStr;
    }

    private static string? TryGetString(JsonObject obj, params string[] candidates)
    {
      foreach (var name in candidates)
      {
        // Find property case-insensitively
        var prop = obj.FirstOrDefault(kvp => string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(prop.Key) && prop.Value is not null)
        {
          var extracted = ExtractString(prop.Value);
          if (!string.IsNullOrWhiteSpace(extracted)) return extracted;
        }
      }
      return null;
    }

    private static int? ExtractInt(JsonNode? node)
    {
      if (node == null) return null;
      if (node is JsonValue jv)
      {
        if (jv.TryGetValue<int>(out var i)) return i;
        if (jv.TryGetValue<long>(out var l)) return (int)l;
        if (jv.TryGetValue<string>(out var s) && int.TryParse(s, out var p)) return p;
      }
      return null;
    }

    private static int? TryGetInt(JsonObject obj, params string[] candidates)
    {
      foreach (var name in candidates)
      {
        var prop = obj.FirstOrDefault(kvp => string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(prop.Key) && prop.Value is not null)
        {
          var extracted = ExtractInt(prop.Value);
          if (extracted.HasValue) return extracted.Value;
        }
      }
      return null;
    }

    public async Task<List<AvaiableDirection>> GetAvaiableOTADirectionsAsync()
    {
      string url = "/Directions/getAvailableDirections";
      using var response = await _client.GetAsync(url);
      if (!response.IsSuccessStatusCode)
      {
        var body = await response.Content.ReadAsStringAsync();
        throw new Exception($"Failed to fetch available directions: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
      }

      var json = await response.Content.ReadAsStringAsync();
      var node = JsonNode.Parse(json);

      var list = new List<AvaiableDirection>();
      if (node is JsonArray arr)
      {
        foreach (var item in arr)
        {
          if (item is not JsonObject obj) continue;

          // Try common property names for origin/destination
          var c1 = TryGetString(obj,
            "Cityone", "cityone", "CityOne", "cityOne", "city_one",
            "originCity", "fromCity", "from", "startCity", "originCityName", "cityOneName",
            "city_name", "from_city", "source", "origin_city_name", "originName");
          var c2 = TryGetString(obj,
            "Citytwo", "citytwo", "CityTwo", "cityTwo", "city_two",
            "destinationCity", "toCity", "to", "endCity", "destinationCityName", "cityTwoName",
            "dest_city", "destination_city", "target", "destination_city_name", "destinationName");

          // Nested shape: { origin: { city_name, city_id }, destination: { ... } }
          var originObj = TryGetObject(obj, "origin", "originCity", "from", "cityOne", "Cityone");
          var destObj = TryGetObject(obj, "destination", "destinationCity", "to", "cityTwo", "Citytwo");
          if (originObj != null)
          {
            c1 ??= TryGetString(originObj, "city_name", "cityName", "name", "Cityone", "city");
          }
          if (destObj != null)
          {
            c2 ??= TryGetString(destObj, "city_name", "cityName", "name", "Citytwo", "city");
          }

          // Enhanced ID extraction with more property name candidates
          var id1 = TryGetInt(obj, 
            "CityoneId", "cityoneid", "cityOneId", "CityOneId", 
            "originCityId", "fromCityId", "city_one_id", "origin_city_id",
            "originId", "origin_id", "fromId", "from_id", "startCityId", "start_city_id",
            "id1", "cityId1", "city_id_1");
          var id2 = TryGetInt(obj, 
            "CitytwoId", "citytwoid", "cityTwoId", "CityTwoId", 
            "destinationCityId", "toCityId", "city_two_id", "destination_city_id",
            "destinationId", "destination_id", "toId", "to_id", "endCityId", "end_city_id",
            "id2", "cityId2", "city_id_2");

          // If IDs are in nested objects, try to extract them
          if (!id1.HasValue && originObj != null)
          {
            id1 = TryGetInt(originObj, "id", "cityId", "city_id", "Id", "ID");
          }
          if (!id2.HasValue && destObj != null)
          {
            id2 = TryGetInt(destObj, "id", "cityId", "city_id", "Id", "ID");
          }

          if (!string.IsNullOrWhiteSpace(c1) && !string.IsNullOrWhiteSpace(c2))
          {
            list.Add(new AvaiableDirection(c1!.Trim(), c2!.Trim(), id1, id2));
          }
        }
      }

      return list;
    }

    private static JsonObject? TryGetObject(JsonObject obj, params string[] candidates)
    {
      foreach (var name in candidates)
      {
        var prop = obj.FirstOrDefault(kvp => string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(prop.Key) && prop.Value is JsonObject childObj)
        {
          return childObj;
        }
      }
      return null;
    }

    private static string NormalizeCity(string? s)
    {
      if (string.IsNullOrWhiteSpace(s)) return string.Empty;
      var str = s.Trim();
      var idx = str.IndexOf('(');
      if (idx >= 0) str = str[..idx];
      str = str
        .Replace("\u200C", string.Empty)
        .Replace("\u200F", string.Empty)
        .Replace("\u200E", string.Empty);
      str = str.Replace('\u064A', '\u06CC').Replace('\u0643', '\u06A9');
      str = str.Replace('\u0629', '\u0647');
      return str.Replace("  ", " ").ToLowerInvariant();
    }

    public async Task<Dictionary<string, int>> GetCityNameIdMapAsync()
    {
      var dirs = await GetAvaiableOTADirectionsAsync();
      var map = new Dictionary<string, int>();
      foreach (var d in dirs)
      {
        if (!string.IsNullOrWhiteSpace(d.Cityone) && d.CityoneId.HasValue)
        {
          var key = NormalizeCity(d.Cityone);
          if (!map.ContainsKey(key)) map[key] = d.CityoneId.Value;
        }
        if (!string.IsNullOrWhiteSpace(d.Citytwo) && d.CitytwoId.HasValue)
        {
          var key = NormalizeCity(d.Citytwo);
          if (!map.ContainsKey(key)) map[key] = d.CitytwoId.Value;
        }
      }
      return map;
    }

    public async Task<bool?> GetTicketIsCancelledAsync(string ticketCode)
    {
      try
      {
        var response = await _client.GetAsync($"/Tickets/getTicketInfo?ticketCode={Uri.EscapeDataString(ticketCode)}");
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        var node = JsonNode.Parse(json);
        if (node == null) return null;

        // Try common field names for cancellation status
        var isCancelledNode = node["isCancelled"] ?? node["IsCancelled"] ?? node["cancelled"] ?? node["Cancelled"];
        if (isCancelledNode != null)
        {
          if (isCancelledNode is JsonValue jv && jv.TryGetValue<bool>(out var b)) return b;
        }

        var statusNode = node["status"] ?? node["Status"] ?? node["ticketStatus"] ?? node["TicketStatus"];
        if (statusNode != null)
        {
          var s = statusNode.ToString().ToLowerInvariant();
          if (s == "cancelled" || s == "canceled" || s == "cancel" || s == "لغو" || s == "لغوشده") return true;
          if (s == "active" || s == "confirmed" || s == "issued") return false;
        }

        return null;
      }
      catch
      {
        return null;
      }
    }

    /// <summary>
    /// Cancels a reserved ticket in ORS. ORS applies the time-based penalty policy and returns the refund amount.
    /// </summary>
    public async Task<CancelTicketResult> CancelTicketAsync(string ticketCode, string? reason = null)
    {
      if (string.IsNullOrWhiteSpace(ticketCode))
      {
        return CancelTicketResult.Fail("کد بلیط نامعتبر است");
      }

      try
      {
        var qs = $"ticketcode={Uri.EscapeDataString(ticketCode.Trim())}";
        if (!string.IsNullOrWhiteSpace(reason))
          qs += $"&reason={Uri.EscapeDataString(reason.Trim())}";

        var response = await _client.PostAsync($"/Tickets/cancelTicket?{qs}", null);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
          var message = ExtractErrorMessage(body) ?? body;
          if (string.IsNullOrWhiteSpace(message))
            message = $"خطای ORS ({(int)response.StatusCode})";
          return CancelTicketResult.Fail(TranslateCancelError(message));
        }

        decimal refund = 0m;
        try
        {
          var node = JsonNode.Parse(body);
          var refundNode = node?["rerfund"] ?? node?["refund"] ?? node?["RefundedAmount"];
          if (refundNode != null && decimal.TryParse(refundNode.ToString(), out var parsed))
            refund = parsed;
        }
        catch (JsonException)
        {
          // Success without parseable body — treat refund as 0
        }

        return CancelTicketResult.Ok(refund);
      }
      catch (Exception ex)
      {
        return CancelTicketResult.Fail(ex.Message);
      }
    }

    private static string? ExtractErrorMessage(string body)
    {
      if (string.IsNullOrWhiteSpace(body)) return null;
      try
      {
        var node = JsonNode.Parse(body);
        if (node == null) return body.Trim();
        return node["error"]?.ToString()
            ?? node["message"]?.ToString()
            ?? node["Message"]?.ToString()
            ?? node["title"]?.ToString()
            ?? body.Trim();
      }
      catch (JsonException)
      {
        return body.Trim().Trim('"');
      }
    }

    private static string TranslateCancelError(string message)
    {
      var m = message ?? string.Empty;
      if (m.Contains("ALREADY CANCELED", StringComparison.OrdinalIgnoreCase))
        return "این بلیط قبلاً لغو شده است";
      if (m.Contains("HAS DONE", StringComparison.OrdinalIgnoreCase))
        return "سفر این بلیط پایان یافته و قابل لغو نیست";
      if (m.Contains("CANCELLATION_BLOCKED", StringComparison.OrdinalIgnoreCase))
        return "امکان لغو به دلیل محدودیت تعداد کنسلی‌های اخیر وجود ندارد. لطفاً با پشتیبانی تماس بگیرید.";
      if (m.Contains("NOT BEEN SUBMITED", StringComparison.OrdinalIgnoreCase) ||
          m.Contains("NOT BEEN SUBMITTED", StringComparison.OrdinalIgnoreCase))
        return "امکان لغو این بلیط از طریق این سامانه وجود ندارد";
      if (m.Contains("NO SUCH A TICKET", StringComparison.OrdinalIgnoreCase))
        return "بلیط در سامانه ORS یافت نشد";
      if (m.Contains("NOT CURRENTLY APPROPRIATE", StringComparison.OrdinalIgnoreCase))
        return "وضعیت سفر برای لغو مناسب نیست";
      if (m.Contains("امکان کنسلی وجود ندارد", StringComparison.OrdinalIgnoreCase))
        return m;
      return string.IsNullOrWhiteSpace(m) ? "لغو بلیط ناموفق بود" : m;
    }

    public async Task ChargeOTABalanceAsync(int amount)
    {
      var content = new StringContent($"charge_amount={amount}", Encoding.UTF8, "application/x-www-form-urlencoded");

      // Make the POST request
      var response = await _client.PostAsync("/OTAManagement/ChargeOTA", content);
      if (!response.IsSuccessStatusCode)
      {
        throw new Exception();
      }
    }
  }
}
